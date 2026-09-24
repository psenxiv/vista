// The checklist model: preparing a checklist, progress, saved answers and the results file. No DOM.
//
// Answers are one object per checklist: step id -> { v, r, note }, where `v` is a step's value,
// `r` a check's result ('pass' | 'fail' | 'na') and `note` what happened instead. Two extra keys
// ride along: `__started` (ISO time) and `__showSkipped` (the page toggle).

const Checklist = (() => {
  const TOOL = 'vista-ingame-checks';

  // Saved per checklist file and per revision, so bumping a revision starts that checklist fresh.
  const storageKey = (file, data) => `${TOOL}:${file}:${data.revision ?? 1}`;

  function loadAnswers(key) {
    try { return JSON.parse(localStorage.getItem(key)) || {}; } catch { return {}; }
  }

  function saveAnswers(key, answers) {
    try { localStorage.setItem(key, JSON.stringify(answers)); } catch {}
  }

  /** Pushes a section's or group's `skip`, and a group's `optional`, down to its steps. */
  function prepare(data) {
    for (const sec of data.sections) for (const g of sec.groups) {
      g.skip = g.skip || sec.skip;
      for (const st of g.steps) {
        st.skip = st.skip || g.skip;
        st.optional = st.optional || g.optional;
      }
    }
    return data;
  }

  const groupsOf = data => data.sections.flatMap(sec => sec.groups);

  const filled = v => v !== undefined && v !== null && String(v).trim() !== '';

  /** Whether a step counts toward "n of m done". Call after prepare(). */
  const isCounted = st => st.t !== 'note' && !st.optional && !st.skip;

  function isAnswered(st, entry) {
    if (!entry) return false;
    switch (st.t) {
      case 'check': return !!entry.r;
      case 'do': return entry.v === true;
      case 'row': return !!entry.v && Object.values(entry.v).some(filled);
      default: return filled(entry.v);
    }
  }

  /** Done and total over counted steps, per group and overall, plus failing checks anywhere. */
  function progress(data, answers) {
    const groups = {};
    let done = 0, total = 0, failed = 0;
    for (const g of groupsOf(data)) {
      let gDone = 0, gTotal = 0;
      for (const st of g.steps) {
        if (st.t === 'check' && answers[st.id]?.r === 'fail') failed++;
        if (!isCounted(st)) continue;
        gTotal++;
        if (isAnswered(st, answers[st.id])) gDone++;
      }
      groups[g.id] = { done: gDone, total: gTotal };
      done += gDone;
      total += gTotal;
    }
    return { done, total, failed, groups };
  }

  const percent = ({ done, total }) => total ? Math.round(done / total * 100) : 0;

  const toNumber = v => filled(v) ? Number(v) : null;

  function resultItem(st, entry) {
    const item = { id: st.id, type: st.t };
    if (st.skip) item.notNeeded = st.skip;
    if (st.optional) item.optional = true;
    switch (st.t) {
      case 'check':
        item.action = st.action;
        item.expect = st.expect;
        item.result = entry.r || 'not run';
        if (entry.note) item.note = entry.note;
        break;
      case 'do':
        item.text = st.text;
        item.done = entry.v === true;
        break;
      case 'row':
        item.label = st.label;
        item.values = Object.fromEntries(st.cols.map(c => [c, toNumber(entry.v?.[c])]));
        break;
      default:
        item.label = st.label;
        item.value = st.t === 'num' ? toNumber(entry.v) : (entry.v ?? null);
        if (st.unit) item.unit = st.unit;
    }
    return item;
  }

  /** The results file: every non-note step's answer, with failing checks also listed up top. */
  function buildResults(file, data, answers, finished = new Date().toISOString()) {
    const out = {
      tool: TOOL,
      checklist: data.title,
      checklistFile: file,
      checksRevision: data.revision ?? 1,
      started: answers.__started,
      finished,
      summary: {},
      failures: [],
      sections: [],
    };
    let required = 0, answered = 0, passed = 0, na = 0;
    for (const sec of data.sections) {
      const S = { id: sec.id, title: sec.title, groups: [] };
      for (const g of sec.groups) {
        const G = { id: g.id, title: g.title, items: [] };
        for (const st of g.steps) {
          if (st.t === 'note') continue;
          const entry = answers[st.id] || {};
          G.items.push(resultItem(st, entry));
          if (st.t === 'check') {
            if (entry.r === 'fail') out.failures.push({ id: st.id, group: `${g.id} ${g.title}`, action: st.action, expect: st.expect, note: entry.note || '' });
            if (entry.r === 'pass') passed++;
            else if (entry.r === 'na') na++;
          }
          if (isCounted(st)) {
            required++;
            if (isAnswered(st, entry)) answered++;
          }
        }
        S.groups.push(G);
      }
      out.sections.push(S);
    }
    out.summary = { required, answered, unanswered: required - answered, passed, failed: out.failures.length, notApplicable: na };
    return out;
  }

  function entryFromItem(item) {
    const entry = {};
    switch (item.type) {
      case 'check':
        if (item.result && item.result !== 'not run') entry.r = item.result;
        if (item.note) entry.note = item.note;
        break;
      case 'do':
        if (item.done) entry.v = true;
        break;
      case 'row': {
        const values = Object.entries(item.values || {}).filter(([, x]) => x !== null);
        if (values.length) entry.v = Object.fromEntries(values.map(([k, x]) => [k, String(x)]));
        break;
      }
      default:
        if (item.value !== null && item.value !== undefined) entry.v = String(item.value);
    }
    return entry;
  }

  /** Answers read back from a results file; the page's show-skipped toggle is kept as it was. */
  function answersFromResults(results, showSkipped) {
    const answers = { __started: results.started || new Date().toISOString(), __showSkipped: showSkipped };
    for (const sec of results.sections || []) for (const g of sec.groups || []) for (const item of g.items || []) {
      const entry = entryFromItem(item);
      if (Object.keys(entry).length) answers[item.id] = entry;
    }
    return answers;
  }

  return {
    TOOL, storageKey, loadAnswers, saveAnswers, prepare,
    isAnswered, progress, percent, buildResults, answersFromResults,
  };
})();
