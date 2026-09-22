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

    @Test fun `first manual cell uses three to four point two volts`() {
        assertEquals(300..420, manualVoltageRangeCentivolts(MutableList(12) { "" }, 0))
    }

    @Test fun `dependent cells use plus or minus point one from first cell`() {
        val draft = MutableList(12) { "" }.apply { this[0] = "4.05" }
        assertEquals(395..415, manualVoltageRangeCentivolts(draft, 1))
        assertEquals(null, manualVoltageRangeCentivolts(MutableList(12) { "" }, 1))
        draft[0] = "4.20"
        assertEquals(410..420, manualVoltageRangeCentivolts(draft, 11))
    }

    @Test fun `changing first cell clamps existing dependent values`() {
        val draft = MutableList(12) { "4.10" }.apply { this[0] = "3.80"; this[1] = "4.05"; this[2] = "3.20" }
        clampDependentCellVoltages(draft)
        assertEquals("3.90", draft[1])
        assertEquals("3.70", draft[2])
    }
}
