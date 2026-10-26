# 0.3.2 — automatic dictionary fallback when a service fails

English → Russian translation now automatically uses the built-in dictionary when the selected provider is offline, rejects authorization, exhausts its quota, returns an invalid/empty response, or takes longer than five seconds. This applies to text translation, screen overlays, selected regions, region monitoring, and word/context lookup. Both local and cloud providers use the same fallback layer.

After a failed request, further translation requests use the dictionary for 15 seconds without contacting that configuration again. The next request after that pause retries the provider. A successful translation returns to the provider automatically. Changing settings or credentials clears the cooldown. No provider choice, key, endpoint, model, or language is overwritten.

Temporary fallback results do not enter the provider's successful translation cache. Translation results and history identify the actual dictionary source and retain a fallback flag. Existing popups reveal the warning when failure occurs after opening; the main window and overlays distinguish an unavailable service from an unconfigured provider. All notices support Russian and English.

The five-second response budget applies only to translation requests eligible for the English–Russian dictionary fallback. Connection tests still use the configured timeout and report the actual service error, never a fake successful dictionary connection. User cancellation cancels the request without triggering fallback or the cooldown. Late adapter responses cannot replace the dictionary result.

The dictionary remains a basic lexical aid with limited context/grammar quality. Unknown words in sentences remain unchanged. A missing single word is reported honestly and cannot be saved as a translated word. Unsupported language pairs retain the actual provider error; an English–Russian database cannot translate arbitrary languages.

Verification uses controlled HTTP failures and hanging requests, the real bundled dictionary, real WPF windows, and controlled capture/OCR. Unit tests cover failure categories, deadlines, cancellation, cooldown/recovery, configuration isolation, invalid responses, unknown words, and cache recovery. Native checks cover text, screen and word flows, history provenance, dynamic RU/EN notices, successful recovery, and connection-test honesty.
