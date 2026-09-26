package com.sarmat.crew.offline

import android.content.Context
import com.sarmat.crew.api.*
import com.sarmat.crew.DEFAULT_DISCHARGED_THRESHOLD_PERCENT
import org.json.JSONArray
import org.json.JSONObject
import java.io.IOException
import java.time.Instant
import java.util.UUID

class CrewRepository(
    context: Context,
    private val remote: CrewApi = CrewApi(context.getSharedPreferences("sarmat_crew", Context.MODE_PRIVATE)),
    val store: OfflineStore = OfflineStore.get(context),
    private val schedule: (Context) -> Unit = { SyncWorker.schedule(it) }
) {
    private val context = context.applicationContext
    val baseUrl get() = remote.baseUrl
    private val scope get() = remote.accountScope ?: ""
    val needsLogin get() = !remote.hasSession()
    val syncing get() = syncingScope != null && syncingScope == scope
    fun hasSession() = remote.accountScope != null || remote.hasSession()
    fun savedCrewLabel() = remote.savedCrewLabel()
    fun lastSync(): String? = store.snapshot(scope).optString("syncedAt").takeIf { it.isNotBlank() }
    fun syncError(): String? = store.error(scope)
    fun pending(): List<PendingOperation> = store.operations(scope)
    fun pendingCount(batteryId: String? = null) = pending().count { batteryId == null || it.body.getString("batteryId") == batteryId }
    fun dischargedThresholdPercent(): Int = store.snapshot(scope).optJSONObject("thresholds")
        ?.optInt("dischargedThresholdPercent", DEFAULT_DISCHARGED_THRESHOLD_PERCENT)
        ?: DEFAULT_DISCHARGED_THRESHOLD_PERCENT

    fun login(url: String, username: String, password: String): CrewUser = synchronized(syncLock) {
        remote.login(url, username, password).also { schedule(context); store.changed() }
    }
    fun logout() = synchronized(syncLock) { remote.logout(); store.changed() }
    fun requestSync() { schedule(context) }

    fun batteries(): List<BatterySummary> {
        val (snapshot, operations) = store.view(scope)
        var rows = remote.parseBatteries(snapshot.optJSONArray("batteries") ?: JSONArray())
        val thresholds = snapshot.optJSONObject("thresholds") ?: JSONObject()
        operations.forEach { operation ->
            val body = operation.body
            if (body.getString("kind") == "active") {
                rows = rows.map { it.copy(activeSince = if (body.getBoolean("active") && it.id == body.getString("batteryId")) body.getString("occurredAt") else null) }
            } else {
                rows = rows.map { item ->
                    if (item.id != body.getString("batteryId") || (item.latestMeasuredAt != null && Instant.parse(item.latestMeasuredAt) > Instant.parse(body.getString("occurredAt")))) item
                    else {
                        val cells = body.getJSONArray("cellVoltages").doubles()
                        val result = runCatching { localMeasurement(item, cells, thresholds.optDouble("warningCellDeltaV", .1), thresholds.optDouble("dangerCellDeltaV", .2)) }.getOrNull()
                        if (result == null) item else item.copy(latestMeasuredAt = body.getString("occurredAt"), latestCells = cells,
                            latestTotalVoltage = result.totalVoltage, latestChargePercent = result.chargePercent,
                            latestDelta = result.cellDelta, latestHealth = result.health)
                    }
                }
            }
        }
        return rows
    }

    fun previewMeasurement(batteryId: String, cells: List<Double>): MeasurementPreview {
        val item = batteries().first { it.id == batteryId }
        val thresholds = store.snapshot(scope).optJSONObject("thresholds")
            ?: throw ApiException("Потрібна перша синхронізація налаштувань")
        return localMeasurement(item, cells, thresholds.getDouble("warningCellDeltaV"), thresholds.getDouble("dangerCellDeltaV"))
    }

    private fun operation(batteryId: String, kind: String) = JSONObject()
        .put("id", UUID.randomUUID().toString()).put("batteryId", batteryId).put("crewId", remote.crewId)
        .put("kind", kind).put("occurredAt", Instant.now().toString())

    fun saveMeasurement(batteryId: String, cells: List<Double>, notes: String, replaceRejectedId: String? = null) {
        check(scope.isNotEmpty()) { "Спочатку увійдіть та синхронізуйте дані" }
        require(notes.trim().length <= 1000) { "Примітка має містити не більше 1000 символів" }
        previewMeasurement(batteryId, cells)
        store.enqueue(scope, operation(batteryId, "measurement").put("cellVoltages", JSONArray(cells)).put("notes", notes.trim()), replaceRejectedId)
        requestSync()
    }

    fun toggleActive(batteryId: String) {
        check(scope.isNotEmpty()) { "Спочатку синхронізуйте дані" }
        check(pending().none { it.body.getString("kind") == "active" && it.state == "blocked" }) { "Спочатку узгодьте конфлікт статусу в хедері" }
        val active = batteries().firstOrNull { it.activeSince != null }
        store.enqueue(scope, operation(batteryId, "active").put("active", active?.id != batteryId)
            .put("expectedActiveId", active?.id ?: JSONObject.NULL).put("expectedActiveSince", active?.activeSince ?: JSONObject.NULL))
        requestSync()
    }

    fun batteryHistory(batteryId: String, offset: Int, limit: Int = 25): BatteryHistoryPage {
        val snapshot = store.snapshot(scope)
        val history = snapshot.optJSONObject("history")?.optJSONArray(batteryId) ?: JSONArray()
        val items = remote.parseHistory(JSONObject().put("items", history).put("nextOffset", JSONObject.NULL)).items.toMutableList()
        val thresholds = snapshot.optJSONObject("thresholds") ?: JSONObject()
        val battery = batteries().firstOrNull { it.id == batteryId }
        pending().filter { it.body.getString("kind") == "measurement" && it.body.getString("batteryId") == batteryId }.forEach {
            val cells = it.body.getJSONArray("cellVoltages").doubles()
            val preview = battery?.let { row -> runCatching { localMeasurement(row, cells, thresholds.optDouble("warningCellDeltaV", .1), thresholds.optDouble("dangerCellDeltaV", .2)) }.getOrNull() }
            items.add(BatteryHistoryItem(it.id, "measurement", it.body.getString("occurredAt"), preview?.totalVoltage, cells,
                preview?.chargePercent, preview?.minCellVoltage, preview?.maxCellVoltage, preview?.cellDelta, preview?.health,
                thresholds.optDouble("warningCellDeltaV", .1), thresholds.optDouble("dangerCellDeltaV", .2),
                null, null, null, null, null,
                "${if (it.state == "blocked") "Не надіслано: ${it.error}" else "Очікує синхронізації"}\n${it.body.optString("notes")}"))
        }
        val sorted = items.distinctBy { it.id }.sortedByDescending { Instant.parse(it.occurredAt) }
        return BatteryHistoryPage(sorted.drop(offset).take(limit), if (offset + limit < sorted.size) offset + limit else null)
    }

    fun saveDraft(batteryId: String, cells: List<String>, notes: String, reference: Int?, rejectedId: String?) {
        store.saveDraft(scope, batteryId, JSONObject().put("cells", JSONArray(cells)).put("notes", notes)
            .put("reference", reference ?: JSONObject.NULL).put("rejectedId", rejectedId ?: JSONObject.NULL))
    }
    fun draft(batteryId: String) = store.draft(scope, batteryId)
    fun acceptServerActiveState() = synchronized(syncLock) {
        check(!needsLogin) { "Увійдіть для отримання актуального статусу" }
        sync()
        check(store.error(scope) == null) { "Потрібен зв’язок, щоб отримати актуальний серверний статус" }
        store.acceptServerActiveState(scope); requestSync()
    }
    fun discardRejected(id: String) = synchronized(syncLock) { store.discardRejected(scope, id); requestSync() }

    /** All remote mutations are replayed in order; independent measurements survive status conflicts. */
    fun sync(): Boolean = synchronized(syncLock) {
        if (!remote.hasSession()) return@synchronized true
        var account = scope
        syncingScope = account; store.changed()
        try {
            remote.ensureIdentity()
            account = scope; syncingScope = account; store.changed()
            remote.checkSyncIdentity()
            var activeBlocked = false
            store.operations(account).forEach { operation ->
                if (operation.state == "sent") return@forEach
                val active = operation.body.getString("kind") == "active"
                if (operation.state == "blocked") { if (active) activeBlocked = true; return@forEach }
                if (active && activeBlocked) return@forEach
                try {
                    val receipt = remote.syncOperation(operation.body)
                    store.mark(account, operation.id, "sent", receipt = receipt)
                } catch (error: ApiException) {
                    if (error.status == null || error.status == 401 || error.status == 408 || error.status == 429 || error.status >= 500) throw error
                    store.mark(account, operation.id, "blocked", error.message)
                    if (active) activeBlocked = true
                }
            }
            val rows = remote.fetchBatteriesJson()
            val thresholds = remote.fetchThresholds()
            val history = JSONObject()
            for (index in 0 until rows.length()) {
                val id = rows.getJSONObject(index).getString("id")
                val all = JSONArray()
                var offset = 0
                do {
                    val page = remote.fetchHistoryJson(id, offset)
                    val items = page.getJSONArray("items")
                    for (entry in 0 until items.length()) all.put(items.getJSONObject(entry))
                    val next = if (page.isNull("nextOffset")) null else page.getInt("nextOffset")
                    if (next == null) break
                    check(next > offset) { "Некоректна сторінка історії" }
                    offset = next
                } while (true)
                history.put(id, all)
            }
            val waiting = store.operations(account).any { it.state != "sent" }
            val snapshot = JSONObject().put("batteries", rows).put("thresholds", thresholds).put("history", history)
            val successfulAt = if (waiting) store.snapshot(account).optString("syncedAt") else Instant.now().toString()
            snapshot.put("syncedAt", successfulAt)
            store.commitSnapshot(account, snapshot)
            !store.operations(account).any { it.state == "queued" && !(activeBlocked && it.body.getString("kind") == "active") }
        } catch (error: Exception) {
            val message = when {
                error is ApiException && error.status == 401 -> "Увійдіть повторно для синхронізації. Локальні записи збережено"
                error is IOException -> "Немає зв’язку. Зміни збережено на телефоні"
                else -> error.message ?: "Не вдалося синхронізувати"
            }
            store.setError(account, message)
            error is ApiException && error.status != null && error.status in 400..499 && error.status !in listOf(408, 429)
        } finally { syncingScope = null; store.changed() }
    }

    companion object {
        private val syncLock = Any()
        @Volatile private var syncingScope: String? = null
    }
}

private fun JSONArray.doubles(): List<Double> = (0 until length()).map(::getDouble)
