package com.sarmat.crew.widget

import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.Bundle
import android.widget.RemoteViews
import com.sarmat.crew.MainActivity
import com.sarmat.crew.R
import com.sarmat.crew.offline.CrewRepository

class BatteryWidgetProvider : AppWidgetProvider() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action == ACTION_SYNC) {
            CrewRepository(context).requestSync()
            return
        }
        if (intent.action == ACTION_REFRESH) {
            val manager = AppWidgetManager.getInstance(context)
            val ids = manager.getAppWidgetIds(ComponentName(context, BatteryWidgetProvider::class.java))
            if (ids.isNotEmpty()) onUpdate(context, manager, ids)
            return
        }
        super.onReceive(context, intent)
    }

    override fun onAppWidgetOptionsChanged(context: Context, manager: AppWidgetManager, appWidgetId: Int, newOptions: Bundle) {
        onUpdate(context, manager, intArrayOf(appWidgetId))
    }

    override fun onUpdate(context: Context, manager: AppWidgetManager, appWidgetIds: IntArray) {
        appWidgetIds.forEach { id ->
            val serviceIntent = Intent(context, BatteryWidgetService::class.java).apply {
                putExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, id)
                data = android.net.Uri.parse(toUri(Intent.URI_INTENT_SCHEME))
            }
            val openApp = PendingIntent.getActivity(
                context,
                id,
                Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP),
                PendingIntent.FLAG_UPDATE_CURRENT or mutableFlag(),
            )
            val refresh = PendingIntent.getBroadcast(context, id,
                Intent(context, BatteryWidgetProvider::class.java).setAction(ACTION_SYNC),
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
            val syncing = CrewRepository(context).syncing
            val views = RemoteViews(context.packageName, R.layout.widget_batteries).apply {
                setImageViewResource(R.id.widgetRefresh, if (syncing) R.drawable.ic_widget_syncing else R.drawable.ic_widget_refresh)
                setContentDescription(R.id.widgetRefresh, context.getString(if (syncing) R.string.widget_syncing else R.string.widget_refresh))
                setOnClickPendingIntent(R.id.widgetRefresh, refresh)
                setOnClickPendingIntent(R.id.widgetHeader, openApp)
                setInt(R.id.widgetBackground, "setImageAlpha", WidgetPreferences.backgroundAlpha(context))
                setRemoteAdapter(R.id.widgetBatteryGrid, serviceIntent)
                setPendingIntentTemplate(R.id.widgetBatteryGrid, openApp)
                setOnClickPendingIntent(R.id.widgetRoot, openApp)
                setOnClickPendingIntent(R.id.widgetEmpty, openApp)
                setEmptyView(R.id.widgetBatteryGrid, R.id.widgetEmpty)
            }
            manager.updateAppWidget(id, views)
            manager.notifyAppWidgetViewDataChanged(id, R.id.widgetBatteryGrid)
        }
    }

    companion object {
        fun updateAll(context: Context) {
            // Keep this asynchronous: store changes can arrive while OfflineStore's monitor is held,
            // while the widget collection refresh reads from that same store.
            context.sendBroadcast(Intent(ACTION_REFRESH).apply {
                component = ComponentName(context, BatteryWidgetProvider::class.java)
                addFlags(Intent.FLAG_RECEIVER_REPLACE_PENDING)
            })
        }

        const val EXTRA_BATTERY_ID = "com.sarmat.crew.widget.BATTERY_ID"
        private const val ACTION_SYNC = "com.sarmat.crew.widget.SYNC"
        private const val ACTION_REFRESH = "com.sarmat.crew.widget.REFRESH"
        private fun mutableFlag(): Int = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) PendingIntent.FLAG_MUTABLE else 0
    }
}
