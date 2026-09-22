import puppeteer from 'puppeteer-core';

(async () => {
  const browser = await puppeteer.launch({
    executablePath: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox', '--window-size=1440,900']
  });

  console.log('--- [VERIFICATION: REGULAR TAB (OWNER)] ---');
  // Context 1: Regular browser context
  const page1 = await browser.newPage();
  await page1.goto('http://localhost:5000', { waitUntil: 'networkidle0' });

  // 1. Create a booking in Tab 1
  await page1.click('#newBookingBtn');
  await page1.waitForSelector('#createBookingModal.active');

  await page1.type('#bookingUserName', 'Verified Tab Owner');
  await page1.type('#bookingUserEmail', 'owner@browser-test.com');
  await page1.type('#bookingStartTime', '17:00');
  await page1.type('#bookingEndTime', '18:00');

  // Submit
  await page1.click('#submitBookingBtn');
  await page1.waitForFunction(() => !document.getElementById('createBookingModal').classList.contains('active'), { timeout: 5000 });
  await new Promise(r => setTimeout(r, 1000));

  // Check localStorage in Tab 1
  const storedTokensTab1 = await page1.evaluate(() => localStorage.getItem('reservepulse_owner_tokens'));
  console.log('Tab 1 localStorage Tokens:', storedTokensTab1);

  // Find the created booking block in Tab 1
  const bookingBlocksTab1 = await page1.$$('.booking-block');
  let targetBlockTab1 = null;
  for (const block of bookingBlocksTab1) {
    const title = await block.$eval('.booking-title', el => el.textContent);
    if (title.includes('Verified Tab Owner')) {
      targetBlockTab1 = block;
      break;
    }
  }

  if (!targetBlockTab1) throw new Error('Created booking not found in Tab 1!');
  await targetBlockTab1.click();
  await page1.waitForSelector('#detailsBookingModal.active');

  const cancelVisibleTab1 = await page1.$eval('#cancelBookingActionBtn', el => el.style.display !== 'none');
  const noticeTextTab1 = await page1.$eval('#accessNoticeText', el => el.textContent);
  console.log('Tab 1 (Owner) Cancel button visible:', cancelVisibleTab1);
  console.log('Tab 1 Access Notice:', noticeTextTab1);
  if (!cancelVisibleTab1) throw new Error('Cancel button should be VISIBLE in owner tab!');

  // Close modal in Tab 1
  await page1.click('#closeDetailsFooterBtn');
  await new Promise(r => setTimeout(r, 500));

  console.log('\n--- [VERIFICATION: INCOGNITO TAB (ANONYMOUS/READ-ONLY)] ---');
  // Context 2: Incognito Context (separate cookie/storage isolation)
  const incognitoContext = await browser.createBrowserContext();
  const page2 = await incognitoContext.newPage();
  await page2.goto('http://localhost:5000', { waitUntil: 'networkidle0' });

  // Check localStorage in Incognito Tab
  const storedTokensTab2 = await page2.evaluate(() => localStorage.getItem('reservepulse_owner_tokens'));
  console.log('Incognito Tab localStorage Tokens:', storedTokensTab2);

  // Find the same booking block in Incognito Tab
  const bookingBlocksTab2 = await page2.$$('.booking-block');
  let targetBlockTab2 = null;
  for (const block of bookingBlocksTab2) {
    const title = await block.$eval('.booking-title', el => el.textContent);
    if (title.includes('Verified Tab Owner')) {
      targetBlockTab2 = block;
      break;
    }
  }

  if (!targetBlockTab2) throw new Error('Booking not found in Incognito Tab!');
  await targetBlockTab2.click();
  await page2.waitForSelector('#detailsBookingModal.active');

  const cancelVisibleTab2 = await page2.$eval('#cancelBookingActionBtn', el => el.style.display !== 'none');
  const noticeTextTab2 = await page2.$eval('#accessNoticeText', el => el.textContent);
  console.log('Incognito Tab Cancel button visible:', cancelVisibleTab2);
  console.log('Incognito Tab Access Notice:', noticeTextTab2);

  if (cancelVisibleTab2) throw new Error('Cancel button should be HIDDEN in incognito tab!');

  // Capture screenshot of the Incognito read-only view for artifacts
  await page2.screenshot({ path: '/Users/kameshwaranrajamani/.gemini/antigravity-ide/brain/9ba98405-4c7f-487b-9950-1c91cf6f588d/incognito_readonly_screenshot.png' });
  console.log('Saved incognito read-only modal screenshot.');

  // Now in Tab 1, test cancelling as owner
  console.log('\n--- [VERIFICATION: OWNER CANCEL EXECUTION IN TAB 1] ---');
  await page1.bringToFront();
  await targetBlockTab1.click();
  await page1.waitForSelector('#detailsBookingModal.active');

  // Capture screenshot of the Owner view showing the Cancel button
  await page1.screenshot({ path: '/Users/kameshwaranrajamani/.gemini/antigravity-ide/brain/9ba98405-4c7f-487b-9950-1c91cf6f588d/owner_details_screenshot.png' });

  await page1.click('#cancelBookingActionBtn');
  await page1.waitForFunction(() => !document.getElementById('detailsBookingModal').classList.contains('active'), { timeout: 5000 });
  console.log('Booking successfully cancelled by owner in Tab 1!');

  await browser.close();
  console.log('\n[ALL BROWSER VERIFICATIONS PASSED SUCCESSFULLY!]');
})();
