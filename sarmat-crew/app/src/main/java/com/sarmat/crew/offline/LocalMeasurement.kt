package com.sarmat.crew.offline

import com.sarmat.crew.api.BatterySummary
import com.sarmat.crew.api.MeasurementPreview
import kotlin.math.roundToInt

internal fun localMeasurement(battery: BatterySummary, cells: List<Double>, warning: Double, danger: Double): MeasurementPreview {
    require(cells.size == battery.cellCount && cells.all {
        it.isFinite() && it >= battery.minVoltage / battery.cellCount && it <= battery.maxVoltage / battery.cellCount + 0.04
    }) { "Перевірте кількість комірок та допустиму напругу" }
    val total = (cells.sum() * 1000).roundToInt() / 1000.0
    val delta = ((cells.max() - cells.min()) * 1000).roundToInt() / 1000.0
    return MeasurementPreview(total, cells.min(), cells.max(), delta,
        ((total - battery.minVoltage) / (battery.maxVoltage - battery.minVoltage) * 100).coerceIn(0.0, 100.0).roundToInt(),
        if (delta >= danger) "danger" else if (delta >= warning) "warning" else "good")
}
