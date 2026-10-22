# Feedback

BetterTalk's **Send feedback** (Settings → Updates, or the tray menu) sends the words the person typed, the
BetterTalk version and the Windows version, nothing else. It sends them to the address in
[`feedback.json`](../feedback.json) at the top of this repository, which the app reads each time it sends, so
the address can change without a new BetterTalk version. While the address is empty, or there is no internet,
feedback is kept on the PC (`feedback-outbox.json` in `%APPDATA%\BetterTalk`) and sent the next time
BetterTalk starts.

The receiver must answer with the plain text `ok` once the feedback is safely stored; anything else counts as
not sent and the feedback is tried again later.

[`google-sheet-receiver.gs`](google-sheet-receiver.gs) is a receiver for a private Google Sheet:

1. Make a new Google Sheet, then choose **Extensions → Apps Script**.
2. Replace the code there with the contents of `google-sheet-receiver.gs` and save.
3. **Deploy → New deployment**, type **Web app**, *Execute as* **Me**, *Who has access* **Anyone**, then
   **Deploy** and allow access.
4. Put the web app address (it ends in `/exec`) in `feedback.json` as `"url"`.
