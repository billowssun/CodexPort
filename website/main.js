const themeColor = document.querySelector('meta[name="theme-color"]');
const darkMode = window.matchMedia('(prefers-color-scheme: dark)');

function syncThemeColor(event) {
  themeColor?.setAttribute('content', event.matches ? '#111411' : '#f2f3ee');
}

syncThemeColor(darkMode);
darkMode.addEventListener('change', syncThemeColor);
