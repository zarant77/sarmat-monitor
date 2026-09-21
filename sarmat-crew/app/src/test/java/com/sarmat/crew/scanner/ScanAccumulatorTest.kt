package com.sarmat.crew.scanner

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ScanAccumulatorTest {
    @Test
    fun `requires repeated reading before exposing a value`() {
        val accumulator = ScanAccumulator(windowSize = 8, minimumVotes = 2)
        val first = accumulator.add(frame(cell = 0, value = 4.18))
        val second = accumulator.add(frame(cell = 0, value = 4.18))

        assertNull(first.cells[0])
        assertEquals(4.18, second.cells[0]?.value ?: 0.0, 0.001)
    }

    @Test
    fun `most frequent value wins over a single high confidence mistake`() {
        val accumulator = ScanAccumulator(windowSize = 8, minimumVotes = 2)
        accumulator.add(frame(cell = 2, value = 4.14, confidence = 0.72))
        accumulator.add(frame(cell = 2, value = 4.19, confidence = 0.99))
        val result = accumulator.add(frame(cell = 2, value = 4.14, confidence = 0.70))

        assertEquals(4.14, result.cells[2]?.value ?: 0.0, 0.001)
    }

    @Test
    fun `completion requires all six stable cells`() {
        val accumulator = ScanAccumulator(windowSize = 8, minimumVotes = 2)
        val complete = ScanFrame(true, (0 until 6).map { CellReading(4.10 + it * 0.01, 0.8) })

        assertFalse(accumulator.add(complete).isComplete)
        assertTrue(accumulator.add(complete).isComplete)
    }

    private fun frame(cell: Int, value: Double, confidence: Double = 0.8): ScanFrame {
        val cells = MutableList<CellReading?>(6) { null }
        cells[cell] = CellReading(value, confidence)
        return ScanFrame(true, cells)
    }
}
