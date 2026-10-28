# 0.4.1 — clearer settings categories and themed help

AI model connections and automatic contextual explanations now live in a separate **AI assistant** settings tab. **Dictionary** keeps local/online dictionary sources, pronunciation, vocabulary media, and Anki. Existing saved settings and credentials are retained; no migration is needed. The quick-start guide and connector documentation point to the new tab.

The **Before you connect** disclosure now uses the application's settings expander style. Its header explicitly inherits the theme foreground, fixing black text and the native Windows glyph in dark mode. The same header is checked in both dark and light themes.

Validation: 119 unit tests, 40 settings checks including all seven tabs and theme colors, and 25 localization checks. Native tests use isolated data and controlled provider responses.
