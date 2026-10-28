# 0.4.0 — a friendlier first translation

- Dismissible dictionary-mode startup reminder, with guide/setup actions and a persistent opt-out in Application settings. A minimized launch uses a tray notification; Windows controls whether that balloon is displayed.
- A bilingual quick-start guide in the sidebar, tray, and Application settings. It explains dictionary versus translator versus assistant, local/cloud choices, fallback, and the actual configured shortcuts.
- A sample-text action that keeps existing text and waits for the user to request translation. Setup and shortcut actions open their specific settings tabs while preserving drafts.
- Three-step instructions for every connector and assistant. Additional notes and official links are available on demand; opening help does not start a service or download a model.
- Connection status distinguishes unverified, checking, successful draft, verified saved configuration, and actionable failure. Edits invalidate checks and cancel requests, so a late success cannot mark modified settings as working.
- Required model/folder fields are considered when deciding whether initial setup is missing. Supplied Yandex Translate folder IDs are forwarded with API keys as well as IAM tokens.

All new UI text supports live Russian/English switching. Startup tips are enabled by default for older settings, without overwriting their existing providers, keys, or language choices. Changing only the tip preference does not clear translation caches or recreate overlays.

See the [implemented UX plan](UX-PLAN-0.4.0.md), [connector instructions](CONNECTORS.md), and [testing guide](TESTING.md). Native verification uses isolated data and controlled HTTP responses; it does not assert that cloud accounts or local servers are live.
