package com.sarmat.crew

import java.util.Locale
import kotlin.math.roundToInt

internal fun isCompleteMeasurement(draft: List<String>, requiredCells: Int = 12): Boolean =
    draft.size == requiredCells && draft.all { it.toDoubleOrNull() != null }

internal fun manualVoltageRangeCentivolts(draft: List<String>, cellIndex: Int, referenceCellIndex: Int?): IntRange? {
    if (cellIndex !in draft.indices) return null
    if (referenceCellIndex == null || cellIndex == referenceCellIndex) return 300..424
    val referenceVoltage = draft.getOrNull(referenceCellIndex)?.toDoubleOrNull() ?: return 300..424
    val center = (referenceVoltage * 100).roundToInt()
    return (center - 10).coerceAtLeast(300)..(center + 10).coerceAtMost(424)
}

internal fun clampDependentCellVoltages(draft: MutableList<String>, referenceCellIndex: Int) {
    if (referenceCellIndex !in draft.indices) return
    for (index in draft.indices) {
        if (index == referenceCellIndex) continue
        val range = manualVoltageRangeCentivolts(draft, index, referenceCellIndex) ?: continue
        val value = draft[index].toDoubleOrNull() ?: continue
        draft[index] = String.format(Locale.US, "%.2f", value.coerceIn(range.first / 100.0, range.last / 100.0))
    }
}
