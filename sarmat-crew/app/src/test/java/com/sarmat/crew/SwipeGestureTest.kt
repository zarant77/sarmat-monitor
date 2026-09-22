package com.sarmat.crew

import org.junit.Assert.assertEquals
import org.junit.Test

class SwipeGestureTest {
    @Test fun `diagonal horizontal swipe remains horizontal in both directions`() {
        assertEquals(GestureDirection.HORIZONTAL, classifyGesture(90f, 45f, 12))
        assertEquals(GestureDirection.HORIZONTAL, classifyGesture(-90f, 45f, 12))
    }

    @Test fun `predominantly vertical movement remains list scrolling`() {
        assertEquals(GestureDirection.VERTICAL, classifyGesture(20f, 80f, 12))
    }

    @Test fun `small movement does not select a direction`() {
        assertEquals(GestureDirection.UNDECIDED, classifyGesture(5f, 5f, 12))
    }
}
