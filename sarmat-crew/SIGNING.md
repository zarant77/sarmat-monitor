# Android release signing

Release APKs and AABs use the same persistent release keystore. CI never generates
a key and never falls back to debug signing. Keep a secure backup of the keystore,
alias and passwords: future updates must use the same signing identity.

## One-time key creation (Windows PowerShell)

Run with JDK 17 `keytool` on PATH. The file is stored outside the repository.
If you already have a production signing key for this app, reuse it instead.

```powershell
$keyDir = Join-Path $env:USERPROFILE 'AndroidSigning'
New-Item -ItemType Directory -Force -Path $keyDir | Out-Null
$keystore = Join-Path $keyDir 'sarmat-crew-release.jks'
if (Test-Path -LiteralPath $keystore) { throw 'Keystore already exists; reuse it.' }
keytool -genkeypair -v -keystore $keystore -storetype JKS -alias sarmat-crew -keyalg RSA -keysize 3072 -validity 10000
if ($LASTEXITCODE -ne 0) { throw 'Key generation failed' }
```

Enter the keystore password, certificate details and key password interactively.
Do not put passwords in command lines or tracked files.

Copy the Base64 value to the clipboard (without printing the private key):

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $env:USERPROFILE 'AndroidSigning/sarmat-crew-release.jks'))) | Set-Clipboard
```

In GitHub repository Settings > Secrets and variables > Actions, create:

| Secret | Value |
| --- | --- |
| `ANDROID_KEYSTORE_BASE64` | The clipboard contents from the command above |
| `ANDROID_KEYSTORE_PASSWORD` | Keystore password |
| `ANDROID_KEY_ALIAS` | `sarmat-crew` (or the alias of your existing key) |
| `ANDROID_KEY_PASSWORD` | Key password; if you accepted the keytool default, this is the keystore password |

Clear the clipboard after saving the secret. Base64 is encoding, not encryption.
Do not commit the keystore, Base64 text or passwords.

## Build and publication

The Android release job checks all four secrets before setup/build, decodes the
keystore into `RUNNER_TEMP`, and passes `ANDROID_KEYSTORE_PATH`,
`ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS` and `ANDROID_KEY_PASSWORD` to
Gradle through environment variables. Local release builds require the same four
environment variables; debug builds do not require them.

Gradle signs both `app-release.apk` and `app-release.aab`. The workflow requires a
nonempty signed APK and verifies it using Android build-tools 35.0.0 `apksigner
verify --verbose`. Missing secrets, missing APKs and invalid signatures fail the
job. The temporary keystore is removed even on failure.

The workflow uploads `SarmatCrew-<tag>.apk` and `SarmatCrew-<tag>.aab` into
`release-part-android`. After both build jobs succeed, `publish` includes them
alongside Monitor and plugins in the combined artifact and the tag's GitHub
Release. Manual runs use `manual-<run number>` and do not create a GitHub Release.

Keep using the same secrets for subsequent releases. Before publishing updates,
also maintain Android `versionCode`/`versionName` in `app/build.gradle.kts`; the
Git tag controls artifact names, not the application's internal version.
