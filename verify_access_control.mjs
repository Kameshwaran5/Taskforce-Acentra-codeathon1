import puppeteer from 'puppeteer-core';

(async () => {
  const browser = await puppeteer.launch({
    executablePath: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox', '--window-size=1440,900']
  });

  const BASE_URL = 'http://localhost:5000';

  // Use a future date 5 months out and a random hour to avoid conflicts
  const now = new Date();
  const testDate = new Date(now.getFullYear(), now.getMonth() + 5, 20);
  const yyyy = testDate.getFullYear();
  const mm = String(testDate.getMonth() + 1).padStart(2, '0');
  const dateStr = `${yyyy}-${mm}-20`;
  // Use a local time window guaranteed to fall within the 8 AM - 8 PM calendar grid in any timezone
  const targetHour = 10 + (now.getMinutes() % 4);
  const startLocal = new Date(`${dateStr}T${String(targetHour).padStart(2, '0')}:00:00`);
  const endLocal = new Date(`${dateStr}T${String(targetHour + 1).padStart(2, '0')}:00:00`);

  // -- Step 1: Create booking via REST API --
  const resJson = await (await fetch(`${BASE_URL}/api/resources`)).json();
  const resourceId = resJson[0].id;

  const createResp = await fetch(`${BASE_URL}/api/bookings`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      resourceId,
      userName: 'Verified Tab Owner',
      userEmail: 'owner@browser-test.com',
      startUtc: startLocal.toISOString(),
      endUtc: endLocal.toISOString()
    })
  });
  const created = await createResp.json();

  if (createResp.status !== 200) {
    console.error('❌ Booking failed:', created);
    await browser.close();
    process.exit(1);
  }

  const bookingId = created.id;
  const ownerToken = created.ownerToken;
  console.log(`✅ Created booking ID: ${bookingId}, OwnerToken: ${ownerToken}`);

  // Helper: navigate to a specific date in the app
  const goToDate = async (page, d) => {
    await page.evaluate((dateVal) => {
      const el = document.getElementById('datePickerInput');
      if (el) {
        el.value = dateVal;
        el.dispatchEvent(new Event('change', { bubbles: true }));
      }
    }, d);
    await new Promise(r => setTimeout(r, 1500)); // wait for calendar to reload
  };

  // -- Step 2: Owner Tab --
  console.log('\n--- [VERIFICATION: REGULAR TAB (OWNER)] ---');
  const page1 = await browser.newPage();
  await page1.setViewport({ width: 1440, height: 900 });
  await page1.goto(BASE_URL, { waitUntil: 'networkidle0' });
  await new Promise(r => setTimeout(r, 800));

  // Inject ownerToken into this tab's localStorage
  await page1.evaluate(({ id, token }) => {
    const existing = JSON.parse(localStorage.getItem('reservepulse_owner_tokens') || '{}');
    existing[String(id)] = token;
    localStorage.setItem('reservepulse_owner_tokens', JSON.stringify(existing));
  }, { id: bookingId, token: ownerToken });

  // Navigate to test date so the booking chip is rendered
  await goToDate(page1, dateStr);

  const storedTokensTab1 = await page1.evaluate(() => localStorage.getItem('reservepulse_owner_tokens'));
  console.log('Tab 1 localStorage:', storedTokensTab1);

  // Find and click the chip
  const chips1 = await page1.$$('.booking-chip');
  let chip1 = null;
  for (const chip of chips1) {
    try {
      const name = await chip.$eval('.chip-attendee-name', el => el.textContent.trim());
      if (name.includes('Verified Tab Owner')) { chip1 = chip; break; }
    } catch (_) {}
  }
  if (!chip1) throw new Error('❌ Booking chip not found in Tab 1!');

  await chip1.click();
  await page1.waitForSelector('#detailsBookingModal.active', { timeout: 5000 });

  const cancelVisible1 = await page1.$eval('#cancelBookingActionBtn', el => el.style.display !== 'none');
  const notice1 = await page1.$eval('#accessNoticeText', el => el.textContent.trim());
  console.log('Tab 1 - Cancel button visible:', cancelVisible1);
  console.log('Tab 1 - Access Notice:', notice1);

  if (!cancelVisible1) throw new Error('❌ Cancel button must be VISIBLE for owner!');
  console.log('✅ Owner tab: Cancel button visible. PASS!');

  await page1.screenshot({ path: '/tmp/owner_details_screenshot.png' });
  console.log('Screenshot → /tmp/owner_details_screenshot.png');

  await page1.evaluate(() => document.getElementById('closeDetailsFooterBtn').click());
  await new Promise(r => setTimeout(r, 600));

  // -- Step 3: Incognito Tab (no ownerToken) --
  console.log('\n--- [VERIFICATION: INCOGNITO TAB (ANONYMOUS/READ-ONLY)] ---');
  const incogCtx = await browser.createBrowserContext();
  const page2 = await incogCtx.newPage();
  await page2.setViewport({ width: 1440, height: 900 });
  await page2.goto(BASE_URL, { waitUntil: 'networkidle0' });
  await new Promise(r => setTimeout(r, 800));

  // Navigate to same date
  await goToDate(page2, dateStr);

  const storedTokensTab2 = await page2.evaluate(() => localStorage.getItem('reservepulse_owner_tokens'));
  console.log('Incognito Tab localStorage:', storedTokensTab2);

  const chips2 = await page2.$$('.booking-chip');
  let chip2 = null;
  for (const chip of chips2) {
    try {
      const name = await chip.$eval('.chip-attendee-name', el => el.textContent.trim());
      if (name.includes('Verified Tab Owner')) { chip2 = chip; break; }
    } catch (_) {}
  }
  if (!chip2) throw new Error('❌ Booking chip not found in Incognito Tab!');

  await chip2.click();
  await page2.waitForSelector('#detailsBookingModal.active', { timeout: 5000 });

  const cancelVisible2 = await page2.$eval('#cancelBookingActionBtn', el => el.style.display !== 'none');
  const notice2 = await page2.$eval('#accessNoticeText', el => el.textContent.trim());
  console.log('Incognito - Cancel button visible:', cancelVisible2);
  console.log('Incognito - Access Notice:', notice2);

  if (cancelVisible2) throw new Error('❌ Cancel button must be HIDDEN in incognito!');
  console.log('✅ Incognito tab: Cancel button hidden. PASS!');

  await page2.screenshot({ path: '/tmp/incognito_readonly_screenshot.png' });
  console.log('Screenshot → /tmp/incognito_readonly_screenshot.png');

  // -- Step 4: Owner cancels the booking (re-query chip to avoid stale reference) --
  console.log('\n--- [VERIFICATION: OWNER CANCEL IN TAB 1] ---');
  await page1.bringToFront();
  await new Promise(r => setTimeout(r, 500));
  const chips1Again = await page1.$$('.booking-chip');
  let chip1Again = null;
  for (const chip of chips1Again) {
    try {
      const name = await chip.$eval('.chip-attendee-name', el => el.textContent.trim());
      if (name.includes('Verified Tab Owner')) { chip1Again = chip; break; }
    } catch (_) {}
  }
  if (!chip1Again) throw new Error('❌ Could not re-find chip for cancellation!');
  await chip1Again.click();
  await page1.waitForSelector('#detailsBookingModal.active', { timeout: 5000 });
  await page1.evaluate(() => document.getElementById('cancelBookingActionBtn').click());
  await page1.waitForFunction(
    () => !document.getElementById('detailsBookingModal').classList.contains('active'),
    { timeout: 6000 }
  );
  console.log('✅ Owner successfully cancelled booking in Tab 1!');

  await browser.close();
  console.log('\n══════════════════════════════════════════════════════════════');
  console.log('  ✅ ALL BROWSER VERIFICATIONS PASSED SUCCESSFULLY!');
  console.log('  ✓ Owner can see & use Cancel button');
  console.log('  ✓ Incognito sees read-only view (no Cancel button)');
  console.log('  ✓ Owner cancellation completed');
  console.log('══════════════════════════════════════════════════════════════');
})();
