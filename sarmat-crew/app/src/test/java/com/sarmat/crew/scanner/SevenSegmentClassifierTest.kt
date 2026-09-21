package com.sarmat.crew.scanner

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class SevenSegmentClassifierTest {
    private val decoder = SevenSegmentDecoder()

    @Test
    fun `recognizes four from relative segment strengths`() {
        val result = decoder.classify(listOf(.03, .24, .22, .02, .03, .20, .25))

        assertEquals(4, result?.first)
    }

    @Test
    fun `recognizes three instead of four when left upper segment is dark`() {
        val result = decoder.classify(listOf(.18, .24, .22, .20, .02, .02, .23))

        assertEquals(3, result?.first)
    }

    @Test
    fun `rejects an empty digit`() {
        assertNull(decoder.classify(List(7) { 0.0 }))
    }
}
