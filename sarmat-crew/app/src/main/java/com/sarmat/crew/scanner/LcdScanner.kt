package com.sarmat.crew.scanner

import org.opencv.core.Core
import org.opencv.core.Mat
import org.opencv.core.MatOfPoint
import org.opencv.core.MatOfPoint2f
import org.opencv.core.Point
import org.opencv.core.Rect
import org.opencv.core.RotatedRect
import org.opencv.core.Size
import org.opencv.imgproc.Imgproc
import java.util.ArrayDeque
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.min

class LcdScanner {
    private val decoder = SevenSegmentDecoder()
    private val normalizedHistory = ArrayDeque<Mat>()
    private var missedFrames = 0

    fun scan(gray: Mat): ScanFrame {
        val guide = guideRect(gray)
        val guideMat = gray.submat(guide)
        val display = findDisplay(guideMat)
        if (display == null) {
            guideMat.release()
            missedFrames++
            if (missedFrames >= 6) clearHistory()
            return ScanFrame(displayFound = false, sourceWidth = gray.cols(), sourceHeight = gray.rows())
        }

        missedFrames = 0
        val normalized = rectify(guideMat, display)
        guideMat.release()
        pushHistory(normalized)
        normalized.release()

        val temporal = normalizedHistory.first().clone()
        normalizedHistory.drop(1).forEach { Core.min(temporal, it, temporal) }
        val decoded = decodeWithVariants(temporal)
        temporal.release()
        return ScanFrame(
            displayFound = true,
            cells = decoded.cells,
            debugRows = rowQuads(display, guide, decoded.rows, gray.cols(), gray.rows()),
            sourceWidth = gray.cols(),
            sourceHeight = gray.rows(),
        )
    }

    fun close() = clearHistory()

    private fun guideRect(gray: Mat): Rect {
        val width = (gray.cols() * 0.82).toInt()
        val height = min((width / 0.67).toInt(), (gray.rows() * 0.88).toInt())
        return Rect(
            (gray.cols() - width) / 2,
            (gray.rows() - height) / 2,
            width,
            height,
        )
    }

    private fun findDisplay(gray: Mat): Array<Point>? {
        val enhanced = Mat()
        val blurred = Mat()
        val threshold = Mat()
        Imgproc.createCLAHE(2.5, Size(8.0, 8.0)).apply(gray, enhanced)
        Imgproc.GaussianBlur(enhanced, blurred, Size(7.0, 7.0), 0.0)
        Imgproc.threshold(blurred, threshold, 0.0, 255.0, Imgproc.THRESH_BINARY + Imgproc.THRESH_OTSU)
        Imgproc.morphologyEx(
            threshold,
            threshold,
            Imgproc.MORPH_CLOSE,
            Imgproc.getStructuringElement(Imgproc.MORPH_RECT, Size(11.0, 11.0)),
        )

        val contours = mutableListOf<MatOfPoint>()
        val hierarchy = Mat()
        Imgproc.findContours(threshold, contours, hierarchy, Imgproc.RETR_LIST, Imgproc.CHAIN_APPROX_SIMPLE)
        hierarchy.release()
        threshold.release()
        blurred.release()
        enhanced.release()

        val frameArea = gray.rows().toDouble() * gray.cols()
        val center = Point(gray.cols() / 2.0, gray.rows() / 2.0)
        var winner: RotatedRect? = null
        var winnerScore = Double.NEGATIVE_INFINITY

        contours.forEach { contour ->
            val contourArea = Imgproc.contourArea(contour)
            if (contourArea >= frameArea * 0.025) {
                val points = MatOfPoint2f(*contour.toArray())
                val rect = Imgproc.minAreaRect(points)
                points.release()
                val longSide = max(rect.size.width, rect.size.height)
                val shortSide = min(rect.size.width, rect.size.height)
                val areaRatio = longSide * shortSide / frameArea
                val aspect = shortSide / longSide.coerceAtLeast(1.0)
                val fill = contourArea / (longSide * shortSide).coerceAtLeast(1.0)
                val dx = abs(rect.center.x - center.x) / gray.cols()
                val dy = abs(rect.center.y - center.y) / gray.rows()
                val bounds = Imgproc.boundingRect(contour)
                val bounded = Rect(
                    bounds.x.coerceIn(0, gray.cols() - 1),
                    bounds.y.coerceIn(0, gray.rows() - 1),
                    min(bounds.width, gray.cols() - bounds.x.coerceIn(0, gray.cols() - 1)),
                    min(bounds.height, gray.rows() - bounds.y.coerceIn(0, gray.rows() - 1)),
                )
                val region = gray.submat(bounded)
                val brightness = Core.mean(region).`val`[0] / 255.0
                region.release()
                if (aspect in 0.38..0.78 && areaRatio in 0.035..0.62 && fill >= 0.38 && brightness >= 0.42 && dx < 0.30 && dy < 0.30) {
                    val aspectScore = 1.0 - abs(aspect - 0.56)
                    val centerScore = 1.0 - dx - dy
                    val sizeScore = min(areaRatio / 0.16, 1.0)
                    val score = aspectScore * 1.5 + centerScore + sizeScore + fill * 2.0 + brightness * 3.0
                    if (score > winnerScore) {
                        winner = rect
                        winnerScore = score
                    }
                }
            }
            contour.release()
        }

        return winner?.let { rect ->
            Array(4) { Point() }.also(rect::points).let(::orderCorners)
        }
    }

    private fun orderCorners(points: Array<Point>): Array<Point> {
        val topLeft = points.minBy { it.x + it.y }
        val bottomRight = points.maxBy { it.x + it.y }
        val topRight = points.maxBy { it.x - it.y }
        val bottomLeft = points.minBy { it.x - it.y }
        return arrayOf(topLeft, topRight, bottomRight, bottomLeft)
    }

    private fun rectify(gray: Mat, corners: Array<Point>): Mat {
        val portrait = edgeLength(corners[0], corners[1]) < edgeLength(corners[1], corners[2])
        val ordered = if (portrait) corners else arrayOf(corners[1], corners[2], corners[3], corners[0])
        val source = MatOfPoint2f(*ordered)
        val destination = MatOfPoint2f(
            Point(0.0, 0.0),
            Point(NORMALIZED_WIDTH - 1.0, 0.0),
            Point(NORMALIZED_WIDTH - 1.0, NORMALIZED_HEIGHT - 1.0),
            Point(0.0, NORMALIZED_HEIGHT - 1.0),
        )
        val transform = Imgproc.getPerspectiveTransform(source, destination)
        val result = Mat()
        Imgproc.warpPerspective(
            gray,
            result,
            transform,
            Size(NORMALIZED_WIDTH.toDouble(), NORMALIZED_HEIGHT.toDouble()),
        )
        source.release()
        destination.release()
        transform.release()
        return result
    }

    private fun decodeWithVariants(gray: Mat): DecodedFrame {
        val enhanced = Mat()
        Imgproc.createCLAHE(3.0, Size(6.0, 6.0)).apply(gray, enhanced)
        val otsu = Mat()
        val adaptive = Mat()
        val blackHat = Mat()
        val blackHatMask = Mat()
        Imgproc.threshold(enhanced, otsu, 0.0, 255.0, Imgproc.THRESH_BINARY_INV + Imgproc.THRESH_OTSU)
        Imgproc.adaptiveThreshold(
            enhanced,
            adaptive,
            255.0,
            Imgproc.ADAPTIVE_THRESH_GAUSSIAN_C,
            Imgproc.THRESH_BINARY_INV,
            31,
            7.0,
        )
        Imgproc.morphologyEx(
            adaptive,
            adaptive,
            Imgproc.MORPH_OPEN,
            Imgproc.getStructuringElement(Imgproc.MORPH_RECT, Size(2.0, 2.0)),
        )

        // Extract locally dark LCD strokes independently of the large lighting
        // gradient caused by glare. This is much more stable than a single global
        // threshold when one side of the display is brighter than the other.
        Imgproc.morphologyEx(
            enhanced,
            blackHat,
            Imgproc.MORPH_BLACKHAT,
            Imgproc.getStructuringElement(Imgproc.MORPH_RECT, Size(17.0, 17.0)),
        )
        Imgproc.threshold(blackHat, blackHatMask, 0.0, 255.0, Imgproc.THRESH_BINARY + Imgproc.THRESH_OTSU)
        Imgproc.morphologyEx(
            blackHatMask,
            blackHatMask,
            Imgproc.MORPH_CLOSE,
            Imgproc.getStructuringElement(Imgproc.MORPH_RECT, Size(3.0, 2.0)),
        )

        val masks = listOf(blackHatMask, adaptive, otsu)
        val detectedRows = masks.map(decoder::detectRows)
        val rowSets = masks.zip(detectedRows).mapNotNull { (mask, rows) ->
            selectBestRowSet(mask, rows)
        }
        val bestRows = rowSets.maxByOrNull { it.score }?.rows
            ?: detectedRows.maxByOrNull { it.size }?.take(CELL_COUNT).orEmpty()
        val variants = rowSets.map { it.readings }
        enhanced.release()
        otsu.release()
        adaptive.release()
        blackHat.release()
        blackHatMask.release()

        val cells = (0 until 6).map { index ->
            val candidates = variants.mapNotNull { it[index] }
            val groups = candidates.groupBy { (it.value * 100).toInt() }
            val winner = groups.maxWithOrNull(
                compareBy<Map.Entry<Int, List<CellReading>>> { it.value.size }
                    .thenBy { entry -> entry.value.sumOf { it.confidence } },
            )?.value.orEmpty()
            if (winner.isEmpty()) null else {
                val agreementBonus = if (winner.size >= 2) 0.10 else 0.0
                val disagreementPenalty = if (winner.size == 1 && candidates.size > 1) 0.68 else 1.0
                CellReading(
                    winner.first().value,
                    min(1.0, (winner.map { it.confidence }.average() + agreementBonus) * disagreementPenalty),
                )
            }
        }
        return DecodedFrame(cells, bestRows)
    }

    /**
     * A border, glare or the Total line can look like another text row. Evaluate
     * every consecutive group of six instead of blindly taking the first six.
     * Real cell rows have nearly equal centre-to-centre spacing and, most
     * importantly, yield more valid 3.xx/4.xx readings.
     */
    private fun selectBestRowSet(mask: Mat, detected: List<Rect>): RowSetCandidate? {
        if (detected.size < CELL_COUNT) return null
        return detected.windowed(CELL_COUNT).map { rows ->
            val readings = decoder.decode(mask, rows)
            val decodedCount = readings.count { it != null }
            val centres = rows.map { it.y + it.height / 2.0 }
            val gaps = centres.zipWithNext { first, second -> second - first }
            val meanGap = gaps.average().coerceAtLeast(1.0)
            val gapDeviation = gaps.map { abs(it - meanGap) / meanGap }.average()
            val meanHeight = rows.map { it.height.toDouble() }.average().coerceAtLeast(1.0)
            val heightDeviation = rows.map { abs(it.height - meanHeight) / meanHeight }.average()
            val confidence = readings.filterNotNull().sumOf { it.confidence }
            RowSetCandidate(
                rows = rows,
                readings = readings,
                score = decodedCount * 10.0 + confidence - gapDeviation * 4.0 - heightDeviation * 2.0,
            )
        }.maxByOrNull { it.score }
    }

    private fun rowQuads(
        display: Array<Point>,
        guide: Rect,
        rows: List<Rect>,
        sourceWidth: Int,
        sourceHeight: Int,
    ): List<ScanQuad> = rows.map { row ->
        val top = row.y.toDouble() / NORMALIZED_HEIGHT
        val bottom = (row.y + row.height).toDouble() / NORMALIZED_HEIGHT
        val left = row.x.toDouble() / NORMALIZED_WIDTH
        val right = (row.x + row.width).toDouble() / NORMALIZED_WIDTH
        ScanQuad(listOf(
            projectedPoint(display, guide, left, top, sourceWidth, sourceHeight),
            projectedPoint(display, guide, right, top, sourceWidth, sourceHeight),
            projectedPoint(display, guide, right, bottom, sourceWidth, sourceHeight),
            projectedPoint(display, guide, left, bottom, sourceWidth, sourceHeight),
        ))
    }

    private fun projectedPoint(
        display: Array<Point>,
        guide: Rect,
        horizontal: Double,
        vertical: Double,
        sourceWidth: Int,
        sourceHeight: Int,
    ): ScanPoint {
        val topX = display[0].x + (display[1].x - display[0].x) * horizontal
        val topY = display[0].y + (display[1].y - display[0].y) * horizontal
        val bottomX = display[3].x + (display[2].x - display[3].x) * horizontal
        val bottomY = display[3].y + (display[2].y - display[3].y) * horizontal
        val x = guide.x + topX + (bottomX - topX) * vertical
        val y = guide.y + topY + (bottomY - topY) * vertical
        return ScanPoint(x / sourceWidth, y / sourceHeight)
    }

    private fun pushHistory(normalized: Mat) {
        normalizedHistory.addLast(normalized.clone())
        while (normalizedHistory.size > HISTORY_SIZE) normalizedHistory.removeFirst().release()
    }

    private fun clearHistory() {
        normalizedHistory.forEach(Mat::release)
        normalizedHistory.clear()
    }

    private fun edgeLength(a: Point, b: Point): Double =
        kotlin.math.hypot(a.x - b.x, a.y - b.y)

    private companion object {
        const val NORMALIZED_WIDTH = 340
        const val NORMALIZED_HEIGHT = 600
        const val CELL_COUNT = 6
        const val HISTORY_SIZE = 4
    }

    private data class DecodedFrame(val cells: List<CellReading?>, val rows: List<Rect>)

    private data class RowSetCandidate(
        val rows: List<Rect>,
        val readings: List<CellReading?>,
        val score: Double,
    )
}
