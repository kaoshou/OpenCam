for (const link of document.querySelectorAll('[data-language]')) {
  link.addEventListener('click', () => {
    try { localStorage.setItem('opencam-language', link.dataset.language); } catch { /* Optional. */ }
  });
}
if (location.pathname === '/OpenCam/' || location.pathname === '/OpenCam/index.html') {
  let saved;
  try { saved = localStorage.getItem('opencam-language'); } catch { /* Optional. */ }
  const language = saved === 'en-US' || saved === 'zh-TW' ? saved : navigator.language.toLowerCase().startsWith('en') ? 'en-US' : 'zh-TW';
  location.replace(`/OpenCam/${language}/`);
}
