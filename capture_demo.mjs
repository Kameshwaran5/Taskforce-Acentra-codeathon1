import puppeteer from 'puppeteer-core';

(async () => {
  const browser = await puppeteer.launch({
    executablePath: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox', '--window-size=1440,900']
  });

  const page = await browser.newPage();
  await page.setViewport({ width: 1440, height: 900 });
  await page.goto('http://localhost:5000', { waitUntil: 'networkidle0' });

  // 1. Capture main calendar
  await page.screenshot({ path: '/Users/kameshwaranrajamani/.gemini/antigravity-ide/brain/9ba98405-4c7f-487b-9950-1c91cf6f588d/calendar_screenshot.png' });
  console.log('Saved main calendar screenshot.');

  // 2. Open Live Conflict Demo
  await page.click('#liveConflictDemoBtn');
  await page.waitForSelector('#conflictDemoModal.active');

  // Click 'Fire Concurrent Requests'
  await page.click('#runSimultaneousRaceBtn');

  // Wait for verdict banner to be displayed
  await page.waitForFunction(() => {
    const banner = document.getElementById('verdictBanner');
    return banner && banner.style.display !== 'none';
  }, { timeout: 10000 });

  // Wait 600ms for UI transitions
  await new Promise(r => setTimeout(r, 600));

  // Capture Live Conflict Demo screenshot
  await page.screenshot({ path: '/Users/kameshwaranrajamani/.gemini/antigravity-ide/brain/9ba98405-4c7f-487b-9950-1c91cf6f588d/conflict_demo_screenshot.png' });
  console.log('Saved conflict demo screenshot.');

  // 3. Close demo modal and open booking details modal
  await page.click('#closeDemoFooterBtn');
  await new Promise(r => setTimeout(r, 400));
  
  // Click first booking block
  const bookingChip = await page.$('.booking-chip');
  if (bookingChip) {
    await bookingChip.click();
    await page.waitForSelector('#detailsBookingModal.active');
    await new Promise(r => setTimeout(r, 400));
    await page.screenshot({ path: '/Users/kameshwaranrajamani/.gemini/antigravity-ide/brain/9ba98405-4c7f-487b-9950-1c91cf6f588d/booking_details_screenshot.png' });
    console.log('Saved booking details modal screenshot.');
  }

  await browser.close();
})();
