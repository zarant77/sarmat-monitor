package com.sarmat.crew.scanner

import org.opencv.core.Core
import org.opencv.core.CvType
import org.opencv.core.Mat
import org.opencv.core.Rect
import kotlin.math.max
import kotlin.math.min

class SevenSegmentDecoder {
    private val patterns = mapOf(
        0 to booleanArrayOf(true, true, true, true, true, true, false),
        1 to booleanArrayOf(false, true, true, false, false, false, false),
        2 to booleanArrayOf(true, true, false, true, true, false, true),
        3 to booleanArrayOf(true, true, true, true, false, false, true),
        4 to booleanArrayOf(false, true, true, false, false, true, true),
        5 to booleanArrayOf(true, false, true, true, false, true, true),
        6 to booleanArrayOf(true, false, true, true, true, true, true),
        7 to booleanArrayOf(true, true, true, false, false, false, false),
        8 to booleanArrayOf(true, true, true, true, true, true, true),
        9 to booleanArrayOf(true, true, true, true, false, true, true),
    )

    fun detectRows(binary: Mat): List<Rect> {
        val left = (binary.cols() * 0.015).toInt()
        val right = (binary.cols() * 0.52).toInt()
        val values = binary.submat(Rect(left, 0, right - left, binary.rows()))
        val projection = Mat()
        Core.reduce(values, projection, 1, Core.REDUCE_SUM, CvType.CV_32S)
        values.release()

        val threshold = (right - left) * 255 * 0.028
        val raw = mutableListOf<IntRange>()
        var start = -1
        for (y in 0 until projection.rows()) {
            val active = projection.get(y, 0)[0] >= threshold
            if (active && start < 0) start = y
            if ((!active || y == projection.rows() - 1) && start >= 0) {
                raw += start..(if (active) y else y - 1)
                start = -1
            }
        }
        projection.release()

        val merged = mutableListOf<IntRange>()
        val maxGap = max(3, (binary.rows() * 0.012).toInt())
        raw.forEach { run ->
            val previous = merged.lastOrNull()
            if (previous != null && run.first - previous.last <= maxGap) merged[merged.lastIndex] = previous.first..run.last
            else merged += run
        }

        return merged.mapNotNull { run ->
            val height = run.last - run.first + 1
            if (height < binary.rows() * 0.025 || height > binary.rows() * 0.145 || run.last >= binary.rows() * 0.84) null
            else {
                val padding = max(2, (height * 0.10).toInt())
                val top = max(0, run.first - padding)
                val bottom = min(binary.rows(), run.last + 1 + padding)
                Rect(left, top, right - left, bottom - top)
            }
        }
    }

    fun decode(binary: Mat, rows: List<Rect>): List<CellReading?> {
        if (rows.size != 6) return List(6) { null }
        return rows.map { decodeRow(binary, it) }
    }

    private fun decodeRow(binary: Mat, bounds: Rect): CellReading? {
        val row = binary.submat(bounds)
        val components = digitComponents(row)
        if (components.size != 3) {
            row.release()
            return null
        }

        val decoded = components.map { rect ->
            val digit = row.submat(rect)
            val result = decodeDigit(digit)
            digit.release()
            result
        }
        row.release()

        if (decoded.any { it == null }) return null
        val digits = decoded.filterNotNull()
        val value = digits[0].first + digits[1].first / 10.0 + digits[2].first / 100.0
        if (value !in 3.0..4.35) return null
        return CellReading(value, digits.map { it.second }.average())
    }

    private fun digitComponents(row: Mat): List<Rect> {
        val projection = Mat()
        Core.reduce(row, projection, 0, Core.REDUCE_SUM, CvType.CV_32S)
        val threshold = row.rows() * 255 * 0.035
        val runs = mutableListOf<IntRange>()
        var start = -1
        for (x in 0 until projection.cols()) {
            val active = projection.get(0, x)[0] >= threshold
            if (active && start < 0) start = x
            if ((!active || x == projection.cols() - 1) && start >= 0) {
                val end = if (active) x else x - 1
                runs += start..end
                start = -1
            }
        }
        projection.release()

        val merged = mutableListOf<IntRange>()
        val maxGap = max(2, (row.cols() * 0.018).toInt())
        for (run in runs) {
            val previous = merged.lastOrNull()
            if (previous != null && run.first - previous.last <= maxGap) {
                merged[merged.lastIndex] = previous.first..run.last
            } else {
                merged += run
            }
        }

        val digits = merged.mapNotNull { run -> componentBounds(row, run) }
            .filter { it.height >= row.rows() * 0.32 && it.width >= 2 }
            // The small "V" at the right can be taller than digit 1. Voltage digits are
            // always the first three tall components, so position is safer than area.
            .sortedBy { it.x }
            .take(3)
        if (digits.size != 3) return emptyList()

        // Use one shared vertical coordinate system for all digits. Cropping every digit
        // to its lit pixels distorts 1 and 4 because they have no top/bottom segments.
        val activeTop = digits.minOf { it.y }
        val activeBottom = digits.maxOf { it.y + it.height }
        val verticalPad = max(2, ((activeBottom - activeTop) * 0.13).toInt())
        val top = max(0, activeTop - verticalPad)
        val bottom = min(row.rows(), activeBottom + verticalPad)
        return digits.map { Rect(it.x, top, it.width, bottom - top) }
    }

    private fun componentBounds(row: Mat, xRange: IntRange): Rect? {
        var minY = row.rows()
        var maxY = -1
        for (y in 0 until row.rows()) {
            for (x in xRange) {
                if (row.get(y, x)[0] > 0) {
                    minY = min(minY, y)
                    maxY = max(maxY, y)
                }
            }
        }
        if (maxY < minY) return null
        val padX = 2
        val padY = 1
        val x = max(0, xRange.first - padX)
        val y = max(0, minY - padY)
        val right = min(row.cols(), xRange.last + 1 + padX)
        val bottom = min(row.rows(), maxY + 1 + padY)
        return Rect(x, y, right - x, bottom - y)
    }

    private fun decodeDigit(digit: Mat): Pair<Int, Double>? {
        val ratio = digit.cols().toDouble() / digit.rows().coerceAtLeast(1)
        if (ratio < 0.30) return 1 to 0.86

        val regions = arrayOf(
            doubleArrayOf(.20, .00, .80, .20), // a
            doubleArrayOf(.68, .08, 1.00, .48), // b
            doubleArrayOf(.68, .52, 1.00, .92), // c
            doubleArrayOf(.20, .80, .80, 1.00), // d
            doubleArrayOf(.00, .52, .32, .92), // e
            doubleArrayOf(.00, .08, .32, .48), // f
            doubleArrayOf(.20, .40, .80, .60), // g
        )
        return classify(regions.map { regionDensity(digit, it) })
    }

    private fun regionDensity(mat: Mat, normalized: DoubleArray): Double {
        val x1 = (normalized[0] * mat.cols()).toInt().coerceIn(0, mat.cols() - 1)
        val y1 = (normalized[1] * mat.rows()).toInt().coerceIn(0, mat.rows() - 1)
        val x2 = (normalized[2] * mat.cols()).toInt().coerceIn(x1 + 1, mat.cols())
        val y2 = (normalized[3] * mat.rows()).toInt().coerceIn(y1 + 1, mat.rows())
        val region = mat.submat(Rect(x1, y1, x2 - x1, y2 - y1))
        val density = Core.countNonZero(region).toDouble() / (region.rows() * region.cols())
        region.release()
        return density
    }

    internal fun classify(densities: List<Double>): Pair<Int, Double>? {
        if (densities.size != 7) return null
        val peak = densities.maxOrNull() ?: return null
        if (peak < 0.025) return null

        // LCD segments are thin and their absolute density changes strongly with focus and
        // thresholding. Compare every segment with the brightest one in the same digit.
        val normalized = densities.map { (it / peak).coerceIn(0.0, 1.0) }
        var bestDigit = -1
        var bestLoss = Double.MAX_VALUE
        var secondLoss = Double.MAX_VALUE
        for ((number, pattern) in patterns) {
            val loss = pattern.indices.map { index ->
                val target = if (pattern[index]) 0.72 else 0.04
                val weight = if (pattern[index]) 1.0 else 0.90
                kotlin.math.abs(normalized[index] - target) * weight
            }.average()
            if (loss < bestLoss) {
                secondLoss = bestLoss
                bestLoss = loss
                bestDigit = number
            } else if (loss < secondLoss) {
                secondLoss = loss
            }
        }

        val separation = (secondLoss - bestLoss).coerceAtLeast(0.0)
        val confidence = (1.0 - bestLoss * 1.55 + separation * 0.8).coerceIn(0.0, 1.0)
        return if (confidence >= 0.50) bestDigit to confidence else null
    }
}
