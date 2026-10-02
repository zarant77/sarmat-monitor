package com.sarmat.crew.widget

import android.appwidget.AppWidgetManager
import android.content.Context
import android.content.Intent
import android.widget.RemoteViews
import android.widget.RemoteViewsService
import com.sarmat.crew.BatteryWidgetLevel
import com.sarmat.crew.R
import com.sarmat.crew.api.BatterySummary
import com.sarmat.crew.batteryWidgetLevel
import com.sarmat.crew.offline.CrewRepository

class BatteryWidgetService : RemoteViewsService() {
    override fun onGetViewFactory(intent: Intent): RemoteViewsFactory = BatteryWidgetFactory(applicationContext, intent.getIntExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, AppWidgetManager.INVALID_APPWIDGET_ID))
}

private class BatteryWidgetFactory(context: Context, private val widgetId: Int) : RemoteViewsService.RemoteViewsFactory {
    private val context = context.applicationContext
    private var compact = false
    private var batteries = emptyList<BatterySummary>()
    private var dischargedThreshold = 50
    private var criticalThreshold = 20

    override fun onCreate() = refresh()
    override fun onDataSetChanged() = refresh()
    override fun onDestroy() { batteries = emptyList() }
    override fun getCount(): Int = batteries.size
    override fun getViewTypeCount(): Int = 2
    override fun hasStableIds(): Boolean = true
    override fun getLoadingView(): RemoteViews? = null
    override fun getItemId(position: Int): Long = batteries[position].id.hashCode().toLong()

    override fun getViewAt(position: Int): RemoteViews? {
        val battery = batteries.getOrNull(position) ?: return null
        val level = batteryWidgetLevel(battery.latestChargePercent, dischargedThreshold, criticalThreshold)
        val icon = when (level) {
            BatteryWidgetLevel.FULL -> R.drawable.ic_widget_battery_full
            BatteryWidgetLevel.LOW -> R.drawable.ic_widget_battery_low
            BatteryWidgetLevel.CRITICAL -> R.drawable.ic_widget_battery_critical
            BatteryWidgetLevel.UNKNOWN -> R.drawable.ic_widget_battery_unknown
        }
        val number = batteryNumber(battery.label, position)
        val isActive = battery.activeSince != null
        return RemoteViews(context.packageName, if (compact) R.layout.widget_battery_item_compact else R.layout.widget_battery_item).apply {
            setImageViewResource(R.id.widgetBatteryIcon, icon)
            setTextViewText(R.id.widgetBatteryNumber, if (isActive) context.getString(R.string.widget_active_battery_number, number) else number)
            setTextColor(R.id.widgetBatteryNumber, context.getColor(if (isActive) R.color.lime else android.R.color.white))
            setContentDescription(
                R.id.widgetBatteryIcon,
                if (isActive) context.getString(R.string.widget_battery_in_drone, battery.label) else battery.label,
            )
            setOnClickFillInIntent(R.id.widgetBatteryItem, Intent().putExtra(BatteryWidgetProvider.EXTRA_BATTERY_ID, battery.id))
        }
    }

    private fun refresh() {
        compact = AppWidgetManager.getInstance(context).getAppWidgetOptions(widgetId)
            .getInt(AppWidgetManager.OPTION_APPWIDGET_MIN_HEIGHT, 72) < 72
        val repository = CrewRepository(context)
        batteries = runCatching { repository.batteries().sortedBy { it.label.lowercase() } }.getOrDefault(emptyList())
        dischargedThreshold = repository.dischargedThresholdPercent()
        criticalThreshold = repository.criticalChargePercent()
    }
}

internal fun batteryNumber(label: String, position: Int): String {
    val digits = Regex("\\d+").find(label)?.value ?: return (position + 1).toString()
    return digits.toIntOrNull()?.toString() ?: digits
}
