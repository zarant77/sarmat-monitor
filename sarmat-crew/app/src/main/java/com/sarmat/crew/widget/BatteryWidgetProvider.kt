package com.sarmat.crew.widget

import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.os.Build
import android.widget.RemoteViews
import com.sarmat.crew.MainActivity
import com.sarmat.crew.R

class BatteryWidgetProvider : AppWidgetProvider() {
    override fun onUpdate(context: Context, manager: AppWidgetManager, appWidgetIds: IntArray) {
        appWidgetIds.forEach { id ->
            val serviceIntent = Intent(context, BatteryWidgetService::class.java).apply {
                putExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, id)
                data = android.net.Uri.parse(toUri(Intent.URI_INTENT_SCHEME))
            }
            val openApp = PendingIntent.getActivity(
                context,
                id,
                Intent(context, MainActivity::class.java),
                PendingIntent.FLAG_UPDATE_CURRENT or mutableFlag(),
            )
            val views = RemoteViews(context.packageName, R.layout.widget_batteries).apply {
                setRemoteAdapter(R.id.widgetBatteryGrid, serviceIntent)
                setPendingIntentTemplate(R.id.widgetBatteryGrid, openApp)
                setEmptyView(R.id.widgetBatteryGrid, R.id.widgetEmpty)
            }
            manager.updateAppWidget(id, views)
            manager.notifyAppWidgetViewDataChanged(id, R.id.widgetBatteryGrid)
        }
    }

    companion object {
        fun updateAll(context: Context) {
            val manager = AppWidgetManager.getInstance(context)
            val ids = manager.getAppWidgetIds(ComponentName(context, BatteryWidgetProvider::class.java))
            if (ids.isNotEmpty()) BatteryWidgetProvider().onUpdate(context, manager, ids)
        }

        private fun mutableFlag(): Int = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) PendingIntent.FLAG_MUTABLE else 0
    }
}
