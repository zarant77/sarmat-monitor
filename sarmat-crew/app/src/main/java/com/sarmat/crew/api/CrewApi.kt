package com.sarmat.crew.api

import android.content.SharedPreferences
import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

class CrewApi(private val preferences: SharedPreferences) {
    var baseUrl: String
        get() = preferences.getString(KEY_BASE_URL, "") ?: ""
        private set(value) = preferences.edit().putString(KEY_BASE_URL, value).apply()

    private var cookie: String?
        get() = preferences.getString(KEY_COOKIE, null)
        set(value) = preferences.edit().apply {
            if (value == null) remove(KEY_COOKIE) else putString(KEY_COOKIE, value)
        }.apply()

    fun hasSession(): Boolean = baseUrl.isNotBlank() && !cookie.isNullOrBlank()
    val accountScope: String? get() = preferences.getString("offline_scope", null)
    val crewId: String get() = preferences.getString("offline_crew_id", "") ?: ""

    fun ensureIdentity() {
        if (accountScope == null) saveIdentity(requestObject("GET", "/api/auth/me"))
    }

    private fun saveIdentity(json: JSONObject) {
        if (json.getString("role") != "CREW") throw ApiException("Потрібен обліковий запис екіпажу")
        preferences.edit().putString("offline_scope", "$baseUrl|${json.getString("id")}|${json.getString("crewId")}")
            .putString("offline_crew_id", json.getString("crewId")).commit()
    }

    fun login(serverUrl: String, username: String, password: String): CrewUser {
        val previous = preferences.all.toMap()
        try {
            baseUrl = normalizeUrl(serverUrl)
            val body = JSONObject().put("username", username).put("password", password)
            val json = requestObject("POST", "/api/auth/login", body, authenticated = false)
            val user = CrewUser(
                username = json.getString("username"),
                role = json.getString("role"),
                crewName = json.optString("crewName", ""),
                crewNumber = if (json.isNull("crewNumber")) null else json.getInt("crewNumber"),
            )
            if (user.role != "CREW") {
                runCatching { logout() }
                throw ApiException("Цей застосунок призначений лише для облікового запису екіпажу")
            }
            preferences.edit()
                .putString(KEY_CREW_NAME, user.crewName)
                .putString(KEY_USERNAME, user.username)
                .putInt(KEY_CREW_NUMBER, user.crewNumber ?: -1)
                .apply()
            saveIdentity(json)
            return user
        } catch (error: Exception) {
            preferences.edit().clear().apply {
                previous.forEach { (key, value) -> when (value) {
                    is String -> putString(key, value)
                    is Int -> putInt(key, value)
                    is Boolean -> putBoolean(key, value)
                } }
            }.commit()
            throw error
        }
    }

    fun savedCrewLabel(): String {
        val name = preferences.getString(KEY_CREW_NAME, "") ?: ""
        val number = preferences.getInt(KEY_CREW_NUMBER, -1)
        return if (number > 0) "№$number · $name" else name
    }

    fun batteries(): List<BatterySummary> {
        val array = requestArray("GET", "/api/batteries")
        return parseBatteries(array)
    }

    fun fetchBatteriesJson(): JSONArray = requestArray("GET", "/api/batteries")
    fun fetchThresholds(): JSONObject = requestObject("GET", "/api/settings/thresholds")
    fun checkSyncIdentity() {
        val identity = requestObject("GET", "/api/auth/me")
        val currentScope = "$baseUrl|${identity.getString("id")}|${identity.getString("crewId")}"
        if (currentScope != accountScope) throw ApiException("Екіпаж облікового запису змінився. Увійдіть повторно", 403)
        try { requestObject("GET", "/api/crew/sync-capabilities") }
        catch (error: ApiException) {
            if (error.status == 404) throw ApiException("Потрібне оновлення бекенду для офлайн-синхронізації", 404)
            throw error
        }
    }
    fun fetchHistoryJson(id: String, offset: Int): JSONObject = requestObject("GET", "/api/batteries/$id/history?offset=$offset&limit=50")
    fun syncOperation(body: JSONObject): JSONObject = requestObject("POST", "/api/crew/sync", body)

    fun parseBatteries(array: JSONArray): List<BatterySummary> {
        return (0 until array.length()).map { index ->
            val item = array.getJSONObject(index)
            val latest = item.optJSONObject("latestMeasurement")
            BatterySummary(
                id = item.getString("id"),
                label = item.getString("label"),
                serialNumber = item.getString("serialNumber"),
                typeName = item.getString("typeName"),
                capacityAh = item.getDouble("capacityAh"),
                minVoltage = item.getDouble("minVoltage"),
                maxVoltage = item.getDouble("maxVoltage"),
                cellCount = item.getInt("cellCount"),
                state = item.getString("state"),
                chemistry = item.getString("chemistry"),
                cycleCount = item.getInt("cycleCount"),
                activeSince = item.optStringOrNull("activeSince"),
                latestMeasuredAt = latest?.optStringOrNull("measuredAt"),
                latestCells = latest?.optJSONArray("cellVoltages")?.let { cells -> (0 until cells.length()).map(cells::getDouble) },
                latestTotalVoltage = latest?.optDoubleOrNull("totalVoltage"),
                latestChargePercent = latest?.optIntOrNull("chargePercent"),
                latestDelta = latest?.optDoubleOrNull("cellDelta"),
                latestHealth = latest?.optString("health"),
            )
        }
    }

    fun toggleActive(batteryId: String) {
        requestObject("POST", "/api/batteries/$batteryId/toggle-active", JSONObject())
    }

    fun previewMeasurement(batteryId: String, cells: List<Double>): MeasurementPreview {
        val module = { values: List<Double> -> JSONObject().put("cells", JSONArray().also { array -> values.forEach(array::put) }) }
        val json = requestObject("POST", "/api/batteries/$batteryId/measurement-preview", JSONObject().put("A", module(cells.take(6))).put("B", module(cells.drop(6))))
        return MeasurementPreview(
            totalVoltage = json.getDouble("combinedTotalVoltage"),
            minCellVoltage = json.getDouble("minCellVoltage"),
            maxCellVoltage = json.getDouble("maxCellVoltage"),
            cellDelta = json.getDouble("cellDelta"),
            chargePercent = json.getInt("chargePercent"),
            health = json.getString("health"),
        )
    }

    fun saveMeasurement(batteryId: String, cells: List<Double>, notes: String) {
        val voltages = JSONArray().also { array -> cells.forEach(array::put) }
        requestObject(
            "POST",
            "/api/batteries/$batteryId/measurements",
            JSONObject().put("cellVoltages", voltages).put("notes", notes),
        )
    }

    fun batteryHistory(batteryId: String, offset: Int, limit: Int = 25): BatteryHistoryPage {
        val json = requestObject("GET", "/api/batteries/$batteryId/history?offset=$offset&limit=$limit")
        return parseHistory(json)
    }

    fun parseHistory(json: JSONObject): BatteryHistoryPage {
        val array = json.getJSONArray("items")
        val items = (0 until array.length()).map { index ->
            val item = array.getJSONObject(index)
            BatteryHistoryItem(
                id = item.getString("id"),
                kind = item.getString("kind"),
                occurredAt = item.getString("occurredAt"),
                totalVoltage = item.optDoubleOrNull("totalVoltage"),
                cellVoltages = item.optJSONArray("cellVoltages")?.let { cells -> (0 until cells.length()).map(cells::getDouble) },
                chargePercent = item.optIntOrNull("chargePercent"),
                minCellVoltage = item.optDoubleOrNull("minCellVoltage"),
                maxCellVoltage = item.optDoubleOrNull("maxCellVoltage"),
                cellDelta = item.optDoubleOrNull("cellDelta"),
                health = item.optStringOrNull("health"),
                warningThresholdV = item.optDoubleOrNull("warningThresholdV"),
                dangerThresholdV = item.optDoubleOrNull("dangerThresholdV"),
                cycleDelta = item.optIntOrNull("cycleDelta"),
                flightMinutes = item.optIntOrNull("flightMinutes"),
                inferred = if (item.has("inferred") && !item.isNull("inferred")) item.getBoolean("inferred") else null,
                fromCrewName = item.optStringOrNull("fromCrewName"),
                toCrewName = item.optStringOrNull("toCrewName"),
                notes = item.optStringOrNull("notes"),
            )
        }
        return BatteryHistoryPage(items, if (json.isNull("nextOffset")) null else json.getInt("nextOffset"))
    }

    fun logout() {
        runCatching { requestObject("POST", "/api/auth/logout", JSONObject()) }
        clearSession()
    }

    fun clearSession() {
        cookie = null
        preferences.edit().remove(KEY_CREW_NAME).remove(KEY_CREW_NUMBER).remove(KEY_USERNAME)
            .remove("offline_scope").remove("offline_crew_id").commit()
    }

    private fun requestObject(method: String, path: String, body: JSONObject? = null, authenticated: Boolean = true): JSONObject =
        JSONObject(request(method, path, body, authenticated))

    private fun requestArray(method: String, path: String): JSONArray = JSONArray(request(method, path, null, true))

    private fun request(method: String, path: String, body: JSONObject?, authenticated: Boolean): String {
        if (baseUrl.isBlank()) throw ApiException("Не вказана адреса сервера")
        val connection = URL("$baseUrl$path").openConnection() as HttpURLConnection
        try {
            connection.requestMethod = method
            connection.connectTimeout = 10_000
            connection.readTimeout = 15_000
            connection.setRequestProperty("Accept", "application/json")
            connection.setRequestProperty("Content-Type", "application/json")
            if (authenticated) cookie?.let { connection.setRequestProperty("Cookie", it) }
            if (body != null) {
                connection.doOutput = true
                connection.outputStream.use { it.write(body.toString().toByteArray(Charsets.UTF_8)) }
            }

            val status = connection.responseCode
            connection.getHeaderField("Set-Cookie")?.substringBefore(';')?.takeIf { it.isNotBlank() }?.let { cookie = it }
            val stream = if (status in 200..299) connection.inputStream else connection.errorStream
            val response = stream?.bufferedReader()?.use { it.readText() }.orEmpty()
            if (status !in 200..299) {
                if (status == 401) cookie = null // Keep offline data and account identity for reauthentication.
                val message = runCatching { JSONObject(response).optString("error") }.getOrNull()
                throw ApiException(message?.takeIf { it.isNotBlank() } ?: "Помилка сервера: $status", status)
            }
            return response.ifBlank { "{}" }
        } finally {
            connection.disconnect()
        }
    }

    private fun normalizeUrl(value: String): String {
        val normalized = value.trim().trimEnd('/')
        if (!normalized.startsWith("http://") && !normalized.startsWith("https://")) {
            throw ApiException("Адреса сервера має починатися з http:// або https://")
        }
        return normalized
    }

    private fun JSONObject.optDoubleOrNull(name: String): Double? = if (isNull(name) || !has(name)) null else getDouble(name)
    private fun JSONObject.optIntOrNull(name: String): Int? = if (isNull(name) || !has(name)) null else getInt(name)
    private fun JSONObject.optStringOrNull(name: String): String? = if (isNull(name) || !has(name)) null else getString(name)

    companion object {
        private const val KEY_BASE_URL = "server_url"
        private const val KEY_COOKIE = "session_cookie"
        private const val KEY_CREW_NAME = "crew_name"
        private const val KEY_CREW_NUMBER = "crew_number"
        private const val KEY_USERNAME = "username"
    }
}

class ApiException(message: String, val status: Int? = null) : Exception(message)
