// SPDX-License-Identifier: AGPL-3.0-or-later
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

const lightbox = document.getElementById('image-lightbox');
if (lightbox && typeof lightbox.showModal === 'function') {
  const image = lightbox.querySelector('img');
  const caption = lightbox.querySelector('figcaption');
  const closeButton = lightbox.querySelector('[data-lightbox-close]');
  let opener;
  let previousOverflow;

  for (const link of document.querySelectorAll('[data-lightbox]')) {
    link.addEventListener('click', event => {
      // Preserve intentional modified-click/browser link actions.
      if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || lightbox.open) return;
      const thumbnail = link.querySelector('img') || link.closest('figure')?.querySelector('img');
      image.setAttribute('src', link.getAttribute('href'));
      image.alt = thumbnail?.alt || link.textContent.trim();
      caption.textContent = image.alt;
      opener = link;
      previousOverflow = document.body.style.overflow;
      lightbox.showModal();
      document.body.style.overflow = 'hidden';
      closeButton.focus();
      event.preventDefault();
    });
  }
  closeButton.addEventListener('click', () => lightbox.close());
  lightbox.addEventListener('click', event => {
    if (event.target === lightbox) lightbox.close();
  });
  lightbox.addEventListener('cancel', event => {
    event.preventDefault();
    lightbox.close();
  });
  lightbox.addEventListener('close', () => {
    document.body.style.overflow = previousOverflow;
    image.removeAttribute('src');
    opener?.focus({preventScroll: true});
  });
}
