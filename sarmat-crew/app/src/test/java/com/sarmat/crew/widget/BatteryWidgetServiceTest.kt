package com.sarmat.crew.widget

import org.junit.Assert.assertEquals
import org.junit.Test

class BatteryWidgetServiceTest {
    @Test fun `uses numeric part of battery label`() {
        assertEquals("12", batteryNumber("Батарея 12", 0))
        assertEquals("7", batteryNumber("B-007", 0))
    }

    @Test fun `falls back to one based position when label has no number`() {
        assertEquals("3", batteryNumber("Резерв", 2))
    }
}
