package com.sarmat.crew

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class MeasurementDraftTest {
    @Test fun `module A fills cells 1 through 6`() {
        val draft = MutableList(12) { "" }
        applyModuleScan(draft, 0, listOf(4.01, 4.02, 4.03, 4.04, 4.05, 4.06))
        assertEquals(listOf("4.01", "4.02", "4.03", "4.04", "4.05", "4.06"), draft.take(6))
        assertTrue(draft.drop(6).all(String::isEmpty))
    }

    @Test fun `module B fills cells 7 through 12`() {
        val draft = MutableList(12) { "" }
        applyModuleScan(draft, 6, listOf(4.11, 4.12, 4.13, 4.14, 4.15, 4.16))
        assertTrue(draft.take(6).all(String::isEmpty))
        assertEquals(listOf("4.11", "4.12", "4.13", "4.14", "4.15", "4.16"), draft.drop(6))
    }

    @Test fun `manual measurement requires all 12 cells`() {
        val draft = MutableList(12) { "4.100" }
        assertTrue(isCompleteMeasurement(draft))
        draft[11] = ""
        assertFalse(isCompleteMeasurement(draft))
    }
}
