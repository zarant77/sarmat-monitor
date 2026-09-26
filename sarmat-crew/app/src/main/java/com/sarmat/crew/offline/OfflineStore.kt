package com.sarmat.crew.offline

import android.content.ContentValues
import android.content.Context
import android.database.sqlite.SQLiteDatabase
import android.database.sqlite.SQLiteOpenHelper
import androidx.lifecycle.MutableLiveData
import org.json.JSONObject

data class PendingOperation(val id: String, val body: JSONObject, val state: String, val error: String?)

class OfflineStore internal constructor(context: Context) : SQLiteOpenHelper(context, "crew-offline.db", null, 1) {
    val changes = MutableLiveData(0L)
    override fun onCreate(db: SQLiteDatabase) {
        db.execSQL("CREATE TABLE snapshots(scope TEXT PRIMARY KEY, data TEXT NOT NULL DEFAULT '{}', error TEXT)")
        db.execSQL("CREATE TABLE operations(seq INTEGER PRIMARY KEY AUTOINCREMENT, scope TEXT NOT NULL, id TEXT NOT NULL UNIQUE, body TEXT NOT NULL, state TEXT NOT NULL DEFAULT 'queued', error TEXT, receipt TEXT)")
        db.execSQL("CREATE INDEX operations_scope ON operations(scope, seq)")
        db.execSQL("CREATE TABLE drafts(scope TEXT NOT NULL, battery TEXT NOT NULL, data TEXT NOT NULL, PRIMARY KEY(scope,battery))")
    }
    override fun onUpgrade(db: SQLiteDatabase, oldVersion: Int, newVersion: Int) = Unit
    fun changed() { changes.postValue(System.nanoTime()) }

    @Synchronized fun snapshot(scope: String): JSONObject = readableDatabase.rawQuery(
        "SELECT data FROM snapshots WHERE scope=?", arrayOf(scope)
    ).use { if (it.moveToFirst()) JSONObject(it.getString(0)) else JSONObject() }

    @Synchronized fun view(scope: String) = snapshot(scope) to operations(scope)

    @Synchronized fun error(scope: String): String? = readableDatabase.rawQuery(
        "SELECT error FROM snapshots WHERE scope=?", arrayOf(scope)
    ).use { if (it.moveToFirst() && !it.isNull(0)) it.getString(0) else null }

    @Synchronized fun setError(scope: String, error: String?) {
        writableDatabase.execSQL("INSERT OR IGNORE INTO snapshots(scope) VALUES (?)", arrayOf(scope))
        writableDatabase.execSQL("UPDATE snapshots SET error=? WHERE scope=?", arrayOf(error, scope))
        changed()
    }

    @Synchronized fun operations(scope: String): List<PendingOperation> = readableDatabase.rawQuery(
        "SELECT id,body,state,error FROM operations WHERE scope=? ORDER BY seq", arrayOf(scope)
    ).use { cursor -> buildList { while (cursor.moveToNext()) add(PendingOperation(
        cursor.getString(0), JSONObject(cursor.getString(1)), cursor.getString(2),
        if (cursor.isNull(3)) null else cursor.getString(3)
    )) } }

    @Synchronized fun enqueue(scope: String, body: JSONObject, replaceRejectedId: String? = null) {
        val db = writableDatabase
        db.beginTransaction()
        try {
            if (replaceRejectedId != null) {
                check(db.delete("operations", "scope=? AND id=? AND state='blocked'", arrayOf(scope, replaceRejectedId)) == 1) {
                    "Запис уже обробляється. Відкрийте історію повторно"
                }
            }
            db.insertOrThrow("operations", null, ContentValues().apply {
                put("scope", scope); put("id", body.getString("id")); put("body", body.toString())
            })
            if (body.getString("kind") == "measurement") db.delete("drafts", "scope=? AND battery=?", arrayOf(scope, body.getString("batteryId")))
            db.setTransactionSuccessful()
        } finally { db.endTransaction() }
        changed()
    }

    @Synchronized fun mark(scope: String, id: String, state: String, error: String? = null, receipt: JSONObject? = null) {
        writableDatabase.execSQL("UPDATE operations SET state=?,error=?,receipt=? WHERE scope=? AND id=?",
            arrayOf(state, error, receipt?.toString(), scope, id))
        changed()
    }

    @Synchronized fun commitSnapshot(scope: String, data: JSONObject) {
        val db = writableDatabase
        db.beginTransaction()
        try {
            db.insertWithOnConflict("snapshots", null, ContentValues().apply {
                put("scope", scope); put("data", data.toString()); putNull("error")
            }, SQLiteDatabase.CONFLICT_REPLACE)
            // Acknowledged edits remain overlaid until the replacement snapshot is durable.
            db.delete("operations", "scope=? AND state='sent'", arrayOf(scope))
            db.setTransactionSuccessful()
        } finally { db.endTransaction() }
        changed()
    }

    @Synchronized fun acceptServerActiveState(scope: String) {
        val blocked = operations(scope).any { it.body.getString("kind") == "active" && it.state == "blocked" }
        check(blocked) { "Немає конфлікту статусу" }
        val db = writableDatabase
        db.beginTransaction()
        try {
            operations(scope).filter { it.body.getString("kind") == "active" && it.state != "sent" }.forEach {
                db.delete("operations", "scope=? AND id=?", arrayOf(scope, it.id))
            }
            db.setTransactionSuccessful()
        } finally { db.endTransaction() }
        changed()
    }

    @Synchronized fun discardRejected(scope: String, id: String) {
        val operation = operations(scope).firstOrNull { it.id == id }
        check(writableDatabase.delete("operations", "scope=? AND id=? AND state='blocked'", arrayOf(scope, id)) == 1) {
            "Запис уже обробляється"
        }
        operation?.body?.getString("batteryId")?.let { battery ->
            if (draft(scope, battery)?.optString("rejectedId") == id) saveDraft(scope, battery, null)
        }
        changed()
    }

    @Synchronized fun saveDraft(scope: String, battery: String, data: JSONObject?) {
        if (data == null) writableDatabase.delete("drafts", "scope=? AND battery=?", arrayOf(scope, battery))
        else writableDatabase.insertWithOnConflict("drafts", null, ContentValues().apply {
            put("scope", scope); put("battery", battery); put("data", data.toString())
        }, SQLiteDatabase.CONFLICT_REPLACE)
    }

    @Synchronized fun draft(scope: String, battery: String): JSONObject? = readableDatabase.rawQuery(
        "SELECT data FROM drafts WHERE scope=? AND battery=?", arrayOf(scope, battery)
    ).use { if (it.moveToFirst()) JSONObject(it.getString(0)) else null }

    companion object {
        @Volatile private var instance: OfflineStore? = null
        fun get(context: Context): OfflineStore = instance ?: synchronized(this) {
            instance ?: OfflineStore(context.applicationContext).also { instance = it }
        }
    }
}
