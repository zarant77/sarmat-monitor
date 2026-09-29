package com.sarmat.crew.widget

import android.content.Context

internal object WidgetPreferences {
    private const val FILE_NAME = "battery_widget"
    private const val BACKGROUND_TRANSPARENCY = "background_transparency"
    private const val DEFAULT_TRANSPARENCY = 15

    fun backgroundTransparency(context: Context): Int = context
        .getSharedPreferences(FILE_NAME, Context.MODE_PRIVATE)
        .getInt(BACKGROUND_TRANSPARENCY, DEFAULT_TRANSPARENCY)
        .coerceIn(0, 100)

    fun setBackgroundTransparency(context: Context, value: Int) {
        context.getSharedPreferences(FILE_NAME, Context.MODE_PRIVATE)
            .edit()
            .putInt(BACKGROUND_TRANSPARENCY, value.coerceIn(0, 100))
            .apply()
    }

    fun backgroundAlpha(context: Context): Int =
        ((100 - backgroundTransparency(context)) * 255 / 100).coerceIn(0, 255)
}
