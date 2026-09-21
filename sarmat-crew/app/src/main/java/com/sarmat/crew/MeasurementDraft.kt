package com.sarmat.crew

import java.util.Locale

internal fun applyModuleScan(draft: MutableList<String>, offset: Int, values: List<Double>) {
    require(values.size == 6) { "A module scan must contain 6 cells" }
    require(offset == 0 || offset == 6) { "Module offset must be 0 or 6" }
    require(draft.size >= offset + values.size) { "Measurement draft is too small" }
    values.forEachIndexed { index, value -> draft[offset + index] = String.format(Locale.US, "%.2f", value) }
}

internal fun isCompleteMeasurement(draft: List<String>, requiredCells: Int = 12): Boolean =
    draft.size == requiredCells && draft.all { it.toDoubleOrNull() != null }
