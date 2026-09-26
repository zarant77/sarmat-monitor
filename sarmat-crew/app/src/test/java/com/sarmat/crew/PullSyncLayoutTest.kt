package com.sarmat.crew

import android.view.MotionEvent
import android.widget.ScrollView
import org.junit.Assert.assertEquals
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.RuntimeEnvironment
import org.robolectric.annotation.Config
import org.robolectric.annotation.LooperMode

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [28])
@LooperMode(LooperMode.Mode.PAUSED)
class PullSyncLayoutTest {
    @Test fun `only long downward pull at top triggers one sync`() {
        val context = RuntimeEnvironment.getApplication()
        val layout = PullSyncLayout(context)
        var scrolled = false
        layout.addView(object : ScrollView(context) { override fun canScrollVertically(direction: Int) = scrolled })
        var count = 0
        val progress = mutableListOf<Boolean?>()
        layout.onRefresh = { count++ }
        layout.onProgress = { progress += it }
        val scale = context.resources.displayMetrics.density
        fun gesture(dx: Float, dy: Float, cancel: Boolean = false) {
            listOf(Triple(MotionEvent.ACTION_DOWN, 0f, 0f), Triple(MotionEvent.ACTION_MOVE, dx * scale, dy * scale),
                Triple(if (cancel) MotionEvent.ACTION_CANCEL else MotionEvent.ACTION_UP, dx * scale, dy * scale)).forEach { (action, x, y) ->
                MotionEvent.obtain(0, 10, action, x, y, 0).also { layout.dispatchTouchEvent(it); it.recycle() }
            }
        }
        gesture(0f, 60f); gesture(200f, 20f); gesture(0f, 200f, true)
        assertEquals(0, count)
        progress.clear()
        gesture(0f, 160f); assertEquals(1, count)
        assertEquals(listOf(true), progress.distinct())
        scrolled = true; gesture(0f, 200f); assertEquals(1, count)
        scrolled = false; layout.refreshing = true; gesture(0f, 200f); assertEquals(1, count)
    }
}
