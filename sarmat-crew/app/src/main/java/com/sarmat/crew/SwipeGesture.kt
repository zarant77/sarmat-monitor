package com.sarmat.crew

internal enum class GestureDirection { UNDECIDED, HORIZONTAL, VERTICAL }

internal fun classifyGesture(dx: Float, dy: Float, touchSlop: Int): GestureDirection {
    val absX = kotlin.math.abs(dx)
    val absY = kotlin.math.abs(dy)
    return when {
        absX > touchSlop && absX >= absY * .7f -> GestureDirection.HORIZONTAL
        absY > touchSlop && absY > absX * 1.4f -> GestureDirection.VERTICAL
        else -> GestureDirection.UNDECIDED
    }
}
