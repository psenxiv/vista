// HTML for the loader, the picker and a checklist, as strings. page.js puts them in the page.

const View = (() => {
  const ESCAPES = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' };
  const esc = s => String(s).replace(/[&<>"]/g, c => ESCAPES[c]);
  // Checklist text supports `code` and nothing else.
  const md = s => esc(s).replace(/`([^`]+)`/g, '<code>$1</code>');

  function loader(reason) {
    const why = reason ? esc(reason) : `The browser will not let a page opened from a file read another file on its own.
         Serve this folder and reload — VS Code's Live Server on <code>index.html</code> does it —
         or just drop a checklist's <code>.json</code> below.`;
    return `
    <div class="loader">
      <h1>Pick a checklist</h1>
      <p>${why}</p>
      <div class="drop">
        <input type="file" accept="application/json,.json">
        <p>or drop the file here</p>
      </div>
    </div>`;
  }

  /** entries: [{ file, data, progress }] */
  function picker(entries) {
    return `
    <div class="picker">
      <h1>Checklists</h1>
      <p>${entries.length} pending. Progress is kept separately for each, so you can leave one part-done.</p>
      ${entries.map(pickerEntry).join('')}
    </div>`;
  }

  function pickerEntry({ file, data, progress }) {
    const { done, total, failed } = progress;
    const status = done === 0 ? 'not started' : done >= total ? 'complete' : `${done} of ${total}`;
    return `<button class="pick" data-file="${esc(file)}">
      <span class="pick-title">${esc(data.title)}</span>
      <span class="pick-meta">${esc(status)}${failed ? ` · <b class="bad">${failed} failing</b>` : ''}</span>
      <span class="track"><span class="fill" style="width:${Checklist.percent(progress)}%"></span></span>
    </button>`;
  }

  /** The whole checklist screen. Counts and the meter are left empty for page.js to fill. */
  function checklist(data, answers, { canGoBack }) {
    const showSkipped = answers.__showSkipped === true;
    return `
    <header><div class="bar">
      <h1>${esc(data.title)}</h1>
      <div class="meter">
        <div class="track"><div class="fill" id="fill"></div></div>
        <div class="lbl"><span id="ptext"></span><span id="ftext" class="failing"></span></div>
      </div>
      <div class="spacer"></div>
      <label class="toggle"><input type="checkbox" data-action="show-skipped" ${showSkipped ? 'checked' : ''}> show what I don't need</label>
      ${canGoBack ? '<button data-action="back">All checklists</button>' : ''}
      <button data-action="expand">Collapse all</button>
    </div></header>
    <main>
      <div class="intro">${(data.intro ?? []).map(p => `<p>${md(p)}</p>`).join('')}</div>
      ${data.sections.map(sec => section(sec, answers, showSkipped)).join('')}
    </main>
    <footer><div class="bar">
      <span class="saved" id="saved">autosaved in this browser</span>
      <div class="spacer"></div>
      <button data-action="reset">Reset</button>
      <button data-action="import">Load results…</button>
      <button class="primary" data-action="download">Download results</button>
    </div></footer>`;
  }

  function section(sec, answers, showSkipped) {
    const body = sec.groups.map(g => group(g, answers, showSkipped)).join('');
    if (!body) return '';
    return `<h2>${esc(sec.title)}${sec.skip ? ' <span class="gtag">skipped</span>' : ''}</h2>
    ${sec.blurb ? `<p class="blurb">${md(sec.blurb)}</p>` : ''}
    ${sec.skip ? `<p class="blurb"><b>Why.</b> ${md(sec.skip)}</p>` : ''}
    ${body}`;
  }

  function group(g, answers, showSkipped) {
    const isHidden = st => !!st.skip && !showSkipped;
    if (g.steps.every(isHidden)) return '';
    return `<details class="group" open>
    <summary>
      <span class="gid">${esc(g.id)}</span>
      <span class="gtitle">${esc(g.title)}</span>
      ${g.optional ? '<span class="gtag">optional</span>' : ''}
      <span class="gcount" data-count="${esc(g.id)}"></span>
    </summary>
    <div class="gbody">
      ${g.need ? `<div class="why"><b>Why.</b> ${md(g.need)}</div>` : ''}
      ${g.feeds ? `<div class="why"><b>Feeds.</b> ${md(g.feeds)}</div>` : ''}
      ${g.steps.map(st => step(st, answers[st.id] || {}, isHidden(st))).join('')}
    </div>
  </details>`;
  }

  function step(st, entry, hidden) {
    const render = STEPS[st.t];
    return render ? render(st, entry, hidden ? ' hidden' : '') : '';
  }

  const optionalMark = st => st.optional ? ' <span class="unit">(optional)</span>' : '';
  const answeredClass = (st, entry) => Checklist.isAnswered(st, entry) ? ' answered' : '';

  const STEPS = {
    note: (st, entry, hide) => `<div class="step note${hide}">${md(st.text)}</div>`,

    do: (st, entry, hide) => {
      const on = entry.v === true;
      return `<div class="step${hide}"><label class="do${on ? ' done' : ''}" data-do="${esc(st.id)}">
      <input type="checkbox" ${on ? 'checked' : ''}><span>${md(st.text)}${optionalMark(st)}</span>
    </label></div>`;
    },

    num: field,
    text: field,

    pick: (st, entry, hide) => `<div class="step${hide}"><div class="field${answeredClass(st, entry)}">
      <span class="lbl">${md(st.label)}${optionalMark(st)}</span>
      <div class="pills">${st.options.map(o =>
        `<button class="pill${entry.v === o ? ' on' : ''}" data-pick="${esc(st.id)}" data-value="${esc(o)}">${esc(o)}</button>`).join('')}</div>
    </div></div>`,

    row: (st, entry, hide) => `<div class="step${hide}"><div class="rowset">
      <span class="lbl">${md(st.label)}${optionalMark(st)}</span>
      ${st.cols.map(c => `<label class="cell"><span>${esc(c)}</span>
        <input type="number" step="any" data-cell="${esc(st.id)}" data-col="${esc(c)}" value="${esc(entry.v?.[c] ?? '')}"></label>`).join('')}
    </div></div>`,

    check: (st, entry, hide) => {
      const failed = entry.r === 'fail';
      const result = (value, text) =>
        `<button class="pill ${value}${entry.r === value ? ' on' : ''}" data-res="${esc(st.id)}" data-value="${value}">${text}</button>`;
      return `<div class="step${hide}${failed ? ' failed' : ''}" data-step="${esc(st.id)}">
      <div class="check">
        <div class="n">${esc(String(st.id).split('.').pop())}</div>
        <div>
          <div class="act">${md(st.action)}</div>
          <div class="exp">${md(st.expect)}</div>
          ${st.why ? `<div class="tag">covers ${esc(st.why)}</div>` : ''}
          ${st.skip ? `<div class="tag">not needed this pass — ${esc(st.skip)}</div>` : ''}
        </div>
        <div class="pills">${result('pass', 'Pass')}${result('fail', 'Fail')}${result('na', 'N/A')}</div>
        ${failed || entry.note ? failNote(st.id, entry.note) : ''}
      </div></div>`;
    },
  };

  // `num` and `text` steps; a `text` step with `long` gets a text area.
  function field(st, entry, hide) {
    const long = st.t === 'text' && st.long;
    const value = esc(entry.v ?? '');
    const placeholder = esc(st.placeholder || '');
    const input = long
      ? `<textarea data-val="${esc(st.id)}" placeholder="${placeholder}">${value}</textarea>`
      : `<input type="${st.t === 'num' ? 'number' : 'text'}" step="any" data-val="${esc(st.id)}"
           value="${value}" placeholder="${placeholder}">`;
    return `<div class="step${hide}"><div class="field${long ? ' long' : ''}${answeredClass(st, entry)}">
      <span class="lbl">${md(st.label)}${optionalMark(st)}</span>${input}
      ${st.unit ? `<span class="unit">${esc(st.unit)}</span>` : ''}
    </div></div>`;
  }

  /** The "what happened instead" box under a check. */
  const failNote = (id, note) => `<div class="note"><input type="text" data-note="${esc(id)}"
            value="${esc(note || '')}" placeholder="what happened instead"></div>`;

  return { loader, picker, checklist, failNote };
})();
