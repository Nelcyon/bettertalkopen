// BetterTalk feedback receiver for a Google Sheet (Extensions > Apps Script, paste this, Deploy > Web app).
// Each piece of feedback sent from BetterTalk's "Send feedback" becomes one row of the sheet's first tab.
// BetterTalk sends only: the feedback words, the BetterTalk version, the Windows version and when it was written.

function doPost(e) {
  var data;
  try {
    data = JSON.parse(e.postData.contents);
  } catch (err) {
    return reply('bad');
  }
  var text = clean(data.text, 4000);
  if (!text) return reply('bad');
  var lock = LockService.getScriptLock();
  lock.waitLock(10000);
  try {
    var sheet = SpreadsheetApp.getActiveSpreadsheet().getSheets()[0];
    if (sheet.getLastRow() === 0) {
      sheet.appendRow(['Received', 'Written (UTC)', 'BetterTalk', 'Windows', 'Feedback']);
      sheet.setFrozenRows(1);
    }
    sheet.appendRow([new Date(), clean(data.written, 40), clean(data.version, 20), clean(data.windows, 60), text]);
  } finally {
    lock.releaseLock();
  }
  return reply('ok');
}

// Opening the address in a browser just says it is working.
function doGet() {
  return reply('BetterTalk feedback receiver is working.');
}

// Plain text only: a leading = + - or @ would otherwise turn the words into a spreadsheet formula.
function clean(value, max) {
  var s = String(value === undefined || value === null ? '' : value).trim().slice(0, max);
  return /^[=+\-@]/.test(s) ? "'" + s : s;
}

function reply(text) {
  return ContentService.createTextOutput(text);
}
