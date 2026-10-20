const $ = id => document.getElementById(id);
let game;
let table;
let original;

async function post(url, body = {}) {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  const value = await response.json();
  if (!response.ok)
    throw Error(value.error || value.detail || 'Operación fallida');
  return value;
}

function msg(id, text, state = 'neutral') {
  $(id).textContent = text;
  $(id).className = `status ${state}`;
}

function changed() {
  return !!table && JSON.stringify(table) !== JSON.stringify(original);
}

function validSeed(raw) {
  if (raw === '')
    return true;
  if (!/^-?\d+$/.test(raw))
    return false;
  const value = Number(raw);
  return Number.isSafeInteger(value) && value >= -2147483648 && value <= 2147483647;
}

function seedValue() {
  const raw = $('rand-seed').value.trim();
  if (!validSeed(raw))
    throw Error('La semilla debe ser un entero con signo de 32 bits.');
  return raw === '' ? null : Number(raw);
}

function randomizerIsCompatible() {
  return !!game && ['XY', 'ORAS'].includes(game.gameVersion);
}

function randomizerValuesValid() {
  return validSeed($('rand-seed').value.trim());
}

function updateRandomizerUi() {
  const panel = $('randomizer');
  const button = $('randomize-run');
  const compatible = randomizerIsCompatible();
  panel.hidden = !compatible;
  panel.classList.toggle('is-disabled', !compatible || !game?.titleId);
  button.disabled = !compatible || !game?.titleId || !randomizerValuesValid();
}

function ui() {
  $('summary').textContent = changed() ? 'Starters modificados' : 'Sin cambios para exportar';
  $('export').disabled = !game?.titleId || !changed();
  updateRandomizerUi();
}

function speciesSelect(groupIndex, index, value) {
  const select = document.createElement('select');
  select.dataset.group = groupIndex;
  select.dataset.index = index;
  for (const species of table.species) {
    const option = new Option(`${species.id} · ${species.name}`, species.id);
    option.selected = species.id === value;
    select.add(option);
  }
  return select;
}

function render() {
  const box = $('groups');
  box.replaceChildren();
  if (!table)
    return;
  table.groups.forEach((group, groupIndex) => {
    const section = document.createElement('section');
    section.className = 'starter-group';
    const title = document.createElement('h2');
    title.textContent = group.name;
    section.append(title);
    const list = document.createElement('div');
    list.className = 'starter-list';
    group.species.forEach((value, index) => {
      const label = document.createElement('label');
      label.className = 'starter-row';
      label.append(document.createTextNode(`Starter ${index + 1}`), speciesSelect(groupIndex, index, value));
      list.append(label);
    });
    section.append(list);
    box.append(section);
  });
  $('warning').textContent = table.warning || '';
  ui();
}

async function load() {
  try {
    game = await post('/api/workspace/inspect', { workspacePath: $('workspace').value });
    table = undefined;
    original = undefined;
    $('groups').replaceChildren();
    $('warning').textContent = '';
    $('open').disabled = false;
    $('editor').classList.remove('is-disabled');
    msg('status', game.titleId ? `${game.gameVersion} cargado.` : `${game.gameVersion} cargado; falta detectar el Title ID.`, game.titleId ? 'success' : 'neutral');
  } catch (error) {
    game = undefined;
    table = undefined;
    original = undefined;
    $('open').disabled = true;
    $('editor').classList.add('is-disabled');
    $('groups').replaceChildren();
    $('warning').textContent = '';
    msg('status', error.message, 'error');
  }
  ui();
}

async function openStarters() {
  try {
    table = await post('/api/editors/starters/table', { workspacePath: $('workspace').value, language: 2 });
    original = structuredClone(table);
    render();
    msg('result', 'Tabla de starters abierta.', 'success');
  } catch (error) {
    msg('result', error.message, 'error');
  }
}

function randomizeRequest(outputDirectory) {
  return {
    workspacePath: $('workspace').value,
    outputDirectory,
    titleId: game.titleId,
    language: 2,
    basicStarter: $('rand-basic').checked,
    keepSameGen: $('rand-same-gen').checked,
    includeLegendary: $('rand-legendary').checked,
    includeMythical: $('rand-mythical').checked,
    seed: seedValue(),
  };
}

async function randomizeAll() {
  $('randomize-run').disabled = true;
  try {
    msg('randomize-result', 'Elige una carpeta para guardar el LayeredFS…');
    const output = await post('/api/workspace/pick-output');
    const result = await post('/api/editors/starters/randomize', randomizeRequest(output.path));
    msg('randomize-result', `Listo. ZIP: ${result.zipPath}`, 'success');
  } catch (error) {
    msg('randomize-result', error.message, 'error');
  } finally {
    updateRandomizerUi();
  }
}

$('browse').onclick = async () => {
  try {
    $('workspace').value = (await post('/api/workspace/pick')).path;
    await load();
  } catch (error) {
    msg('status', error.message, 'error');
  }
};
$('load').onclick = load;
$('open').onclick = openStarters;
$('groups').onchange = event => {
  const group = event.target.dataset.group;
  const index = event.target.dataset.index;
  if (group === undefined || index === undefined)
    return;
  table.groups[+group].species[+index] = +event.target.value;
  ui();
};
$('export').onclick = async () => {
  try {
    const output = await post('/api/workspace/pick-output');
    const result = await post('/api/editors/starters/export', {
      workspacePath: $('workspace').value,
      outputDirectory: output.path,
      titleId: game.titleId,
      groups: table.groups,
      language: 2,
    });
    original = structuredClone(table);
    msg('result', `Listo. ZIP: ${result.zipPath}`, 'success');
    ui();
  } catch (error) {
    msg('result', error.message, 'error');
  }
};
$('randomize-run').onclick = randomizeAll;
$('rand-seed').addEventListener('input', updateRandomizerUi);
ui();
