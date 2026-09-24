// The page: loads the checklists, shows a screen, and keeps the answers in step with the inputs.

const page = {
  checklists: [],  // [{ file, data }] listed in cases/manifest.json
  file: null,      // the open checklist's file name
  data: null,      // the open checklist
  key: null,       // its localStorage key
  answers: {},     // its answers; see checklist.js
};

const root = document.getElementById('app');

/* ---------- loading ---------- */

async function fetchJson(path) {
  const res = await fetch(path, { cache: 'no-store' });
  if (!res.ok) throw new Error(`${path}: ${res.status}`);
  try { return await res.json(); } catch { throw new Error(`${path}: not valid JSON`); }
}

async function boot() {
  root.addEventListener('click', onClick);
  root.addEventListener('change', onChange);
  root.addEventListener('input', onInput);
  try {
    const manifest = await fetchJson('cases/manifest.json');
    page.checklists = await Promise.all((manifest.checklists ?? []).map(async file =>
      ({ file, data: Checklist.prepare(await fetchJson(`cases/${file}`)) })));
  } catch (e) {
    // Opened as a file, fetch always fails; say so. Served, the error itself is the useful part.
    showLoader(location.protocol === 'file:' ? null : `Could not load ${e.message}.`);
    return;
  }
  if (page.checklists.length === 0) showLoader('The manifest lists no checklists.');
  else if (page.checklists.length === 1) openChecklist(page.checklists[0].file, page.checklists[0].data);
  else showPicker();
}

async function readJsonFile(file) {
  try { return JSON.parse(await file.text()); } catch { alert('That is not JSON I can read.'); return null; }
}

async function openDroppedFile(file) {
  if (!file) return;
  const data = await readJsonFile(file);
  if (data) openChecklist(file.name, Checklist.prepare(data));
}

/* ---------- screens ---------- */

function showLoader(reason) {
  root.innerHTML = View.loader(reason);
  const drop = root.querySelector('.drop');
  drop.ondragover = e => { e.preventDefault(); drop.classList.add('over'); };
  drop.ondragleave = () => drop.classList.remove('over');
  drop.ondrop = e => { e.preventDefault(); drop.classList.remove('over'); openDroppedFile(e.dataTransfer.files[0]); };
}

function showPicker() {
  document.title = 'Vista checks';
  root.innerHTML = View.picker(page.checklists.map(({ file, data }) =>
    ({ file, data, progress: Checklist.progress(data, Checklist.loadAnswers(Checklist.storageKey(file, data))) })));
}

function openChecklist(file, data) {
  page.file = file;
  page.data = data;
  page.key = Checklist.storageKey(file, data);
  page.answers = Checklist.loadAnswers(page.key);
  page.answers.__started ||= new Date().toISOString();
  document.title = data.title;
  renderChecklist();
}

function renderChecklist() {
  root.innerHTML = View.checklist(page.data, page.answers, { canGoBack: page.checklists.length > 1 });
  updateProgress();
}

function updateProgress() {
  const progress = Checklist.progress(page.data, page.answers);
  for (const el of root.querySelectorAll('[data-count]')) {
    const { done, total } = progress.groups[el.dataset.count];
    el.textContent = `${done} / ${total}`;
    el.classList.toggle('done', total > 0 && done === total);
  }
  root.querySelector('#fill').style.width = `${Checklist.percent(progress)}%`;
  root.querySelector('#ptext').textContent = `${progress.done} of ${progress.total} done`;
  root.querySelector('#ftext').textContent = progress.failed ? `${progress.failed} failing` : '';
}

/* ---------- answers ---------- */

const entryFor = id => (page.answers[id] ||= {});

function save() {
  Checklist.saveAnswers(page.key, page.answers);
  root.querySelector('#saved').textContent = `saved ${new Date().toLocaleTimeString()}`;
  updateProgress();
}

function replaceAnswers(answers) {
  page.answers = answers;
  Checklist.saveAnswers(page.key, answers);
  renderChecklist();
}

/* ---------- events (bound once, on #app) ---------- */

function onClick(e) {
  const el = e.target.closest('[data-file], [data-pick], [data-res], [data-action]');
  if (!el) return;
  const { file, pick, res, action } = el.dataset;
  if (file) {
    const hit = page.checklists.find(c => c.file === file);
    openChecklist(hit.file, hit.data);
  } else if (pick) choosePick(el, pick);
  else if (res) chooseResult(el, res);
  else ACTIONS[action]?.(el);
}

const ACTIONS = {
  back: showPicker,
  expand: toggleGroups,
  reset: resetAnswers,
  import: importResults,
  download: downloadResults,
};

function onChange(e) {
  const t = e.target;
  if (t.type === 'file') return openDroppedFile(t.files[0]);
  if (t.dataset.action === 'show-skipped') {
    page.answers.__showSkipped = t.checked;
    save();
    renderChecklist();
    return;
  }
  const doLabel = t.closest('[data-do]');
  if (doLabel) {
    entryFor(doLabel.dataset.do).v = t.checked;
    doLabel.classList.toggle('done', t.checked);
    save();
  }
}

function onInput(e) {
  const t = e.target;
  const { val, note, cell, col } = t.dataset;
  if (val) {
    entryFor(val).v = t.value;
    t.closest('.field').classList.toggle('answered', t.value.trim() !== '');
  } else if (note) {
    entryFor(note).note = t.value;
  } else if (cell) {
    (entryFor(cell).v ||= {})[col] = t.value;
  } else {
    return;
  }
  save();
}

// Clicking the chosen option again clears it, for picks and check results alike.
function choosePick(button, id) {
  const entry = entryFor(id);
  entry.v = entry.v === button.dataset.value ? undefined : button.dataset.value;
  for (const p of button.parentElement.children) p.classList.toggle('on', p.dataset.value === entry.v);
  button.closest('.field').classList.toggle('answered', entry.v !== undefined);
  save();
}

function chooseResult(button, id) {
  const entry = entryFor(id);
  entry.r = entry.r === button.dataset.value ? undefined : button.dataset.value;
  for (const p of button.parentElement.children) p.classList.toggle('on', p.dataset.value === entry.r);
  const stepEl = button.closest('[data-step]');
  const failed = entry.r === 'fail';
  stepEl.classList.toggle('failed', failed);
  const note = stepEl.querySelector('[data-note]');
  if (failed && !note) {
    stepEl.querySelector('.check').insertAdjacentHTML('beforeend', View.failNote(id, ''));
    stepEl.querySelector('[data-note]').focus();
  } else if (!failed && note && !note.value) {
    note.parentElement.remove();
  }
  save();
}

function toggleGroups(button) {
  const groups = [...root.querySelectorAll('.group')];
  const collapse = groups.every(g => g.open);
  for (const g of groups) g.open = !collapse;
  button.textContent = collapse ? 'Expand all' : 'Collapse all';
}

function resetAnswers() {
  if (!confirm('Clear every answer on this checklist?')) return;
  replaceAnswers({ __started: new Date().toISOString(), __showSkipped: page.answers.__showSkipped });
}

/* ---------- results files ---------- */

function downloadResults() {
  const results = Checklist.buildResults(page.file, page.data, page.answers);
  const stamp = new Date().toISOString().slice(0, 16).replace(/[:T]/g, '-');
  const a = document.createElement('a');
  a.href = URL.createObjectURL(new Blob([JSON.stringify(results, null, 2)], { type: 'application/json' }));
  a.download = `${(page.file || 'checks').replace(/\.json$/i, '')}-results-${stamp}.json`;
  a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 1000);
}

function importResults() {
  const input = Object.assign(document.createElement('input'), { type: 'file', accept: 'application/json,.json' });
  input.onchange = async () => {
    if (!input.files[0]) return;
    const results = await readJsonFile(input.files[0]);
    if (!results) return;
    if (results.tool !== Checklist.TOOL) { alert('That is not a results file from this page.'); return; }
    replaceAnswers(Checklist.answersFromResults(results, page.answers.__showSkipped));
  };
  input.click();
}

boot();
