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

    @Test fun `any first manual cell uses full voltage range`() {
        val draft = MutableList(12) { "" }
        assertEquals(300..422, manualVoltageRangeCentivolts(draft, 0, null))
        assertEquals(300..422, manualVoltageRangeCentivolts(draft, 6, null))
    }

    @Test fun `dependent cells use plus or minus point one from chosen reference cell`() {
        val draft = MutableList(12) { "" }.apply { this[6] = "4.05" }
        assertEquals(395..415, manualVoltageRangeCentivolts(draft, 0, 6))
        assertEquals(300..422, manualVoltageRangeCentivolts(draft, 6, 6))
        draft[6] = "4.22"
        assertEquals(412..422, manualVoltageRangeCentivolts(draft, 11, 6))
    }

    @Test fun `changing reference cell clamps existing dependent values`() {
        val draft = MutableList(12) { "4.10" }.apply { this[6] = "3.80"; this[1] = "4.05"; this[2] = "3.20" }
        clampDependentCellVoltages(draft, 6)
        assertEquals("3.90", draft[1])
        assertEquals("3.70", draft[2])
        assertEquals("3.80", draft[6])
    }
}
