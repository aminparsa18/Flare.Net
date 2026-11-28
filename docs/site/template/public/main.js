// docfx "modern" template extension for the Flare docs site (built by scripts/build-docs-site.py).
// Each language is its own docfx site: English at the site root, translations in /ru/, /fr/,
// /zh-CN/, with identical page paths, so switching language keeps the reader on the same page.

const GITHUB = 'https://github.com/aminparsa18/Flare.Net';
const LANGS = [
  { code: 'en', dir: '', label: 'English', suffix: '' },
  { code: 'ru', dir: 'ru/', label: 'Русский', suffix: '.ru' },
  { code: 'fr', dir: 'fr/', label: 'Français', suffix: '.fr' },
  { code: 'zh-CN', dir: 'zh-CN/', label: '中文', suffix: '.zh-CN' },
];
const EDIT_LABEL = { en: 'Edit this page', ru: 'Редактировать', fr: 'Modifier cette page', 'zh-CN': '编辑此页' };

function context() {
  const current = LANGS.find((l) => l.code === document.documentElement.lang) ?? LANGS[0];
  const rel = document.querySelector('meta[name="docfx:rel"]')?.getAttribute('content') ?? '';
  const langRoot = new URL(rel || './', location.href);
  const siteRoot = new URL(current.dir ? '../' : './', langRoot);
  let page = location.href.slice(langRoot.href.length).split(/[?#]/)[0];
  if (page === '' || page.endsWith('/')) page += 'index.html';
  return { current, siteRoot, page };
}

function addLanguagePicker({ current, siteRoot, page }) {
  const host = document.querySelector('#navpanel') ?? document.querySelector('header nav');
  if (!host) return;
  const select = document.createElement('select');
  select.className = 'form-select form-select-sm flare-lang';
  select.setAttribute('aria-label', 'Language');
  select.style.width = 'auto';
  select.style.marginInlineStart = '0.75rem';
  for (const l of LANGS) select.add(new Option(l.label, l.code, false, l.code === current.code));
  select.addEventListener('change', () => {
    const target = LANGS.find((l) => l.code === select.value);
    location.href = new URL(target.dir + page + location.hash, siteRoot).href;
  });
  host.appendChild(select);
}

function addEditLink({ current, page }) {
  const article = document.querySelector('article');
  if (!article || !page.endsWith('.html')) return;
  const stem = page.slice(0, -'.html'.length);
  if (stem === 'index') return; // the home page is generated from docs/site/strings.json, not one page
  // The home page and section landing pages are generated from the docs index (README).
  const generated = stem === 'index' || stem.endsWith('/index');
  const source = generated ? `README${current.suffix}.md` : `${stem}${current.suffix}.md`;
  const a = document.createElement('a');
  a.href = `${GITHUB}/blob/main/docs/${source}`;
  a.className = 'flare-edit d-print-none';
  a.innerHTML = `<i class="bi bi-pencil-square"></i> ${EDIT_LABEL[current.code] ?? EDIT_LABEL.en}`;
  a.style.display = 'inline-block';
  a.style.marginTop = '2rem';
  article.appendChild(a);
}

export default {
  iconLinks: [{ icon: 'github', href: GITHUB, title: 'GitHub' }],
  start: () => {
    const ctx = context();
    addLanguagePicker(ctx);
    addEditLink(ctx);
  },
};
