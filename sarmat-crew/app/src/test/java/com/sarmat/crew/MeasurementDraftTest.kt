package com.sarmat.crew

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class MeasurementDraftTest {
    @Test fun `continuous digits are split into cell voltages`() {
        assertEquals(VoltageDigitStream(listOf(422, 421, 420), ""), parseVoltageDigitStream("422421420"))
        assertEquals(VoltageDigitStream(listOf(422), "42"), parseVoltageDigitStream("42242"))
        assertEquals(VoltageDigitStream(listOf(422, 421), ""), parseVoltageDigitStream("4.22 4,21"))
    }

    @Test fun `unsupported voltage input is rejected`() {
        assertEquals(null, parseVoltageDigitStream("4x22"))
    }

    @Test fun `manual measurement requires all 12 cells`() {
        val draft = MutableList(12) { "4.100" }
        assertTrue(isCompleteMeasurement(draft))
        draft[11] = ""
        assertFalse(isCompleteMeasurement(draft))
    }

    @Test fun `any first manual cell uses full voltage range`() {
        val draft = MutableList(12) { "" }
        assertEquals(300..424, manualVoltageRangeCentivolts(draft, 0, null))
        assertEquals(300..424, manualVoltageRangeCentivolts(draft, 6, null))
    }

    @Test fun `dependent cells use plus or minus point one from chosen reference cell`() {
        val draft = MutableList(12) { "" }.apply { this[6] = "4.05" }
        assertEquals(395..415, manualVoltageRangeCentivolts(draft, 0, 6))
        assertEquals(300..424, manualVoltageRangeCentivolts(draft, 6, 6))
        draft[6] = "4.24"
        assertEquals(414..424, manualVoltageRangeCentivolts(draft, 11, 6))
    }

    @Test fun `changing reference cell clamps existing dependent values`() {
        val draft = MutableList(12) { "4.10" }.apply { this[6] = "3.80"; this[1] = "4.05"; this[2] = "3.20" }
        clampDependentCellVoltages(draft, 6)
        assertEquals("3.90", draft[1])
        assertEquals("3.70", draft[2])
        assertEquals("3.80", draft[6])
    }
}
