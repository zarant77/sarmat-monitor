package com.sarmat.crew.widget

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
    override fun onGetViewFactory(intent: Intent): RemoteViewsFactory = BatteryWidgetFactory(applicationContext)
}

private class BatteryWidgetFactory(context: Context) : RemoteViewsService.RemoteViewsFactory {
    private val context = context.applicationContext
    private var batteries = emptyList<BatterySummary>()
    private var dischargedThreshold = 50
    private var criticalThreshold = 20

    override fun onCreate() = refresh()
    override fun onDataSetChanged() = refresh()
    override fun onDestroy() { batteries = emptyList() }
    override fun getCount(): Int = batteries.size
    override fun getViewTypeCount(): Int = 1
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
        return RemoteViews(context.packageName, R.layout.widget_battery_item).apply {
            setImageViewResource(R.id.widgetBatteryIcon, icon)
            setContentDescription(R.id.widgetBatteryIcon, battery.label)
            setOnClickFillInIntent(R.id.widgetBatteryIcon, Intent())
        }
    }

    private fun refresh() {
        val repository = CrewRepository(context)
        batteries = runCatching { repository.batteries().sortedBy { it.label.lowercase() } }.getOrDefault(emptyList())
        dischargedThreshold = repository.dischargedThresholdPercent()
        criticalThreshold = repository.criticalChargePercent()
    }
}
