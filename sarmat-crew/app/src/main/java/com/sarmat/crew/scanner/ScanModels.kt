package com.sarmat.crew.scanner

data class CellReading(
    val value: Double,
    val confidence: Double,
)

data class ScanFrame(
    val displayFound: Boolean,
    val cells: List<CellReading?> = List(6) { null },
    val debugRows: List<ScanQuad> = emptyList(),
    val sourceWidth: Int = 0,
    val sourceHeight: Int = 0,
)

data class ScanPoint(val x: Double, val y: Double)
data class ScanQuad(val points: List<ScanPoint>)

data class StableScan(
    val displayFound: Boolean,
    val cells: List<CellReading?>,
) {
    val isComplete: Boolean = cells.all { it != null }
}
