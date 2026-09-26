package com.sarmat.crew

import android.content.Context
import android.util.AttributeSet
import android.view.MotionEvent
import android.view.ViewConfiguration
import android.widget.FrameLayout
import kotlin.math.abs

/** Observes touches before battery cards so their horizontal swipe remains independent. */
class PullSyncLayout(context: Context, attrs: AttributeSet? = null) : FrameLayout(context, attrs) {
    var onRefresh: (() -> Unit)? = null
    var onProgress: ((Boolean?) -> Unit)? = null
    var refreshing = false
    private var startX = 0f
    private var startY = 0f
    private var eligible = false
    private var pulling = false
    private var distance = 0f
    private val trigger = 140 * resources.displayMetrics.density
    private val slop = ViewConfiguration.get(context).scaledTouchSlop

    override fun dispatchTouchEvent(event: MotionEvent): Boolean {
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                startX = event.x; startY = event.y; distance = 0f; pulling = false
                eligible = !refreshing && getChildAt(0)?.canScrollVertically(-1) == false
            }
            MotionEvent.ACTION_POINTER_DOWN -> { eligible = false; if (pulling) { reset(); return true } }
            MotionEvent.ACTION_MOVE -> {
                val dx = event.x - startX; val dy = event.y - startY
                if (!pulling && (abs(dx) > slop && abs(dx) >= abs(dy) || dy < -slop)) eligible = false
                if (eligible && !pulling && dy > slop && dy > abs(dx) * 1.5f) {
                    pulling = true
                    val cancel = MotionEvent.obtain(event).apply { action = MotionEvent.ACTION_CANCEL }
                    super.dispatchTouchEvent(cancel); cancel.recycle()
                }
                if (pulling) {
                    distance = dy.coerceAtLeast(0f)
                    getChildAt(0)?.translationY = (distance * .25f).coerceAtMost(trigger * .4f)
                    onProgress?.invoke(distance >= trigger)
                    return true
                }
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> if (pulling) {
                val refresh = event.actionMasked == MotionEvent.ACTION_UP && distance >= trigger && !refreshing
                reset()
                if (refresh) { performClick(); onRefresh?.invoke() }
                return true
            }
        }
        return super.dispatchTouchEvent(event)
    }
    private fun reset() {
        pulling = false; eligible = false
        getChildAt(0)?.animate()?.translationY(0f)?.setDuration(150)?.start()
        onProgress?.invoke(null)
    }
    override fun performClick(): Boolean { super.performClick(); return true }
}
