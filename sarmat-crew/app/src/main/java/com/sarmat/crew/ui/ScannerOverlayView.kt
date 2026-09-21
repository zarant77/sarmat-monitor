package com.sarmat.crew.ui

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.util.AttributeSet
import android.view.View
import com.sarmat.crew.R
import com.sarmat.crew.scanner.ScanQuad

class ScannerOverlayView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
) : View(context, attrs) {
    enum class State { SEARCHING, READING, RECOGNIZED }

    private var state = State.SEARCHING
    private var detectedRows: List<ScanQuad> = emptyList()
    private var sourceWidth = 0
    private var sourceHeight = 0
    private val shadePaint = Paint().apply { color = Color.argb(105, 0, 0, 0) }
    private val linePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeWidth = resources.displayMetrics.density * 3
        strokeCap = Paint.Cap.SQUARE
    }
    private val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        textAlign = Paint.Align.CENTER
        textSize = resources.displayMetrics.scaledDensity * 13
        typeface = android.graphics.Typeface.DEFAULT_BOLD
        letterSpacing = 0.12f
    }
    private val rowPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.FILL_AND_STROKE
        strokeWidth = resources.displayMetrics.density * 2
    }
    private val rowLabelPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.WHITE
        textSize = resources.displayMetrics.scaledDensity * 11
        typeface = android.graphics.Typeface.DEFAULT_BOLD
    }

    fun setState(newState: State) {
        if (state != newState) {
            state = newState
            invalidate()
        }
    }

    fun setDetectedRows(rows: List<ScanQuad>, imageWidth: Int, imageHeight: Int) {
        detectedRows = rows
        sourceWidth = imageWidth
        sourceHeight = imageHeight
        invalidate()
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val guideWidth = width * 0.82f
        val guideHeight = minOf(guideWidth / 0.67f, height * 0.88f)
        val guide = RectF(
            (width - guideWidth) / 2,
            (height - guideHeight) / 2,
            (width + guideWidth) / 2,
            (height + guideHeight) / 2,
        )

        canvas.drawRect(0f, 0f, width.toFloat(), guide.top, shadePaint)
        canvas.drawRect(0f, guide.bottom, width.toFloat(), height.toFloat(), shadePaint)
        canvas.drawRect(0f, guide.top, guide.left, guide.bottom, shadePaint)
        canvas.drawRect(guide.right, guide.top, width.toFloat(), guide.bottom, shadePaint)

        val color = when (state) {
            State.SEARCHING -> context.getColor(R.color.scanner_red)
            State.READING -> context.getColor(R.color.scanner_yellow)
            State.RECOGNIZED -> context.getColor(R.color.scanner_green)
        }
        linePaint.color = color
        textPaint.color = color
        drawCorners(canvas, guide)
        drawDetectedRows(canvas, color)

        val label = when (state) {
            State.SEARCHING -> context.getString(R.string.state_searching)
            State.READING -> context.getString(R.string.state_reading)
            State.RECOGNIZED -> context.getString(R.string.state_recognized)
        }
        canvas.drawText(label, width / 2f, guide.top - 18 * resources.displayMetrics.density, textPaint)
    }

    private fun drawDetectedRows(canvas: Canvas, color: Int) {
        if (sourceWidth <= 0 || sourceHeight <= 0) return
        val scale = maxOf(width.toFloat() / sourceWidth, height.toFloat() / sourceHeight)
        val offsetX = (width - sourceWidth * scale) / 2f
        val offsetY = (height - sourceHeight * scale) / 2f
        rowPaint.color = Color.argb(55, Color.red(color), Color.green(color), Color.blue(color))
        rows@ for ((index, quad) in detectedRows.withIndex()) {
            if (quad.points.size != 4) continue@rows
            val path = Path()
            quad.points.forEachIndexed { pointIndex, point ->
                val x = offsetX + point.x.toFloat() * sourceWidth * scale
                val y = offsetY + point.y.toFloat() * sourceHeight * scale
                if (pointIndex == 0) path.moveTo(x, y) else path.lineTo(x, y)
            }
            path.close()
            canvas.drawPath(path, rowPaint)
            val anchor = quad.points[0]
            canvas.drawText(
                "${index + 1}",
                offsetX + anchor.x.toFloat() * sourceWidth * scale + 4 * resources.displayMetrics.density,
                offsetY + anchor.y.toFloat() * sourceHeight * scale + 13 * resources.displayMetrics.density,
                rowLabelPaint,
            )
        }
    }

    private fun drawCorners(canvas: Canvas, rect: RectF) {
        val length = 32 * resources.displayMetrics.density
        canvas.drawLine(rect.left, rect.top, rect.left + length, rect.top, linePaint)
        canvas.drawLine(rect.left, rect.top, rect.left, rect.top + length, linePaint)
        canvas.drawLine(rect.right, rect.top, rect.right - length, rect.top, linePaint)
        canvas.drawLine(rect.right, rect.top, rect.right, rect.top + length, linePaint)
        canvas.drawLine(rect.left, rect.bottom, rect.left + length, rect.bottom, linePaint)
        canvas.drawLine(rect.left, rect.bottom, rect.left, rect.bottom - length, linePaint)
        canvas.drawLine(rect.right, rect.bottom, rect.right - length, rect.bottom, linePaint)
        canvas.drawLine(rect.right, rect.bottom, rect.right, rect.bottom - length, linePaint)
    }
}
