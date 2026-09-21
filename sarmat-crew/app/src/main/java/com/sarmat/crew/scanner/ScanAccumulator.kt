package com.sarmat.crew.scanner

import java.util.ArrayDeque
import kotlin.math.roundToInt

/** Stabilizes noisy frame-by-frame OCR by voting independently for every cell. */
class ScanAccumulator(
    private val windowSize: Int = 24,
    private val minimumVotes: Int = 2,
) {
    private val frames = ArrayDeque<ScanFrame>()

    fun add(frame: ScanFrame): StableScan {
        frames.addLast(frame)
        while (frames.size > windowSize) frames.removeFirst()

        return StableScan(
            displayFound = frame.displayFound,
            cells = (0 until 6).map(::bestReading),
        )
    }

    fun clear() = frames.clear()

    private fun bestReading(index: Int): CellReading? {
        val candidates = frames.mapNotNull { it.cells.getOrNull(index) }
        if (candidates.isEmpty()) return null

        // Readings differing only by floating-point noise belong to one 0.01 V bucket.
        val groups = candidates.groupBy { (it.value * 100.0).roundToInt() }
        val winner = groups.maxWithOrNull(
            compareBy<Map.Entry<Int, List<CellReading>>> { it.value.size }
                .thenBy { entry -> entry.value.sumOf { it.confidence } },
        ) ?: return null

        if (winner.value.size < minimumVotes) return null
        val confidence = winner.value.map { it.confidence }.average()
        return CellReading(winner.key / 100.0, confidence)
    }
}
