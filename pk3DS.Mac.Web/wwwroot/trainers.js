const $ = id => document.getElementById(id);
let game, catalog, data, original;

async function post(url, body = {}) {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  const result = await response.json();
  if (!response.ok) throw Error(result.error || result.detail || 'Operación fallida');
  return result;
}

function msg(id, text, state = 'neutral') {
  $(id).textContent = text;
  $(id).className = `status ${state}`;
}

function changed() { return !!data && JSON.stringify(data) !== JSON.stringify(original); }
function ui() {
  $('summary').textContent = changed() ? 'Entrenador modificado' : 'Sin cambios para exportar';
  $('export').disabled = !game?.titleId || !changed();
}

function isGen6() {
  return game?.gameVersion === 'XY' || game?.gameVersion === 'ORAS' || game?.gameVersion === 'ORASDEMO';
}

function select(items, value, dataset) {
  const input = document.createElement('select');
  Object.assign(input.dataset, dataset);
  for (const item of items) {
    const option = new Option(`${item.id} · ${item.name}`, item.id);
    option.selected = item.id === value;
    input.add(option);
  }
  return input;
}

function number(value, min, max, dataset, disabled = false) {
  const input = document.createElement('input');
  input.type = 'number';
  input.min = min;
  input.max = max;
  input.value = value;
  input.disabled = disabled;
  Object.assign(input.dataset, dataset);
  return input;
}

function textInput(value, dataset, disabled = false) {
  const input = document.createElement('input');
  input.type = 'text';
  input.value = value ?? '';
  input.disabled = disabled;
  Object.assign(input.dataset, dataset);
  return input;
}

function label(text, input) {
  const node = document.createElement('label');
  node.textContent = text;
  node.append(input);
  return node;
}

function card(title) {
  const node = document.createElement('section');
  node.className = 'trainer-card';
  const heading = document.createElement('h2');
  heading.textContent = title;
  node.append(heading);
  return node;
}

function render() {
  const box = $('details');
  box.replaceChildren();
  if (!data) return;

  const gen6 = isGen6();
  const modes = gen6
    ? [
        { id: 0, name: 'Individual' }, { id: 1, name: 'Dobles' },
        { id: 2, name: 'Triple' }, { id: 3, name: 'Rotación' },
        ...(game.gameVersion === 'ORAS' || game.gameVersion === 'ORASDEMO' ? [{ id: 4, name: 'Horda' }] : []),
      ]
    : [{ id: 0, name: 'Individual' }, { id: 1, name: 'Dobles' }, { id: 2, name: 'Múltiple' }];

  const head = card('Datos del entrenador');
  head.classList.add('trainer-meta');
  head.append(
    label('Nombre', textInput(data.name, { field: 'name' })),
    label('Clase', select(catalog.classes, data.trainerClass, { field: 'trainerClass' })),
    label('Modo', select(modes, data.mode, { field: 'mode' })),
    label('IA', number(data.ai, 0, 255, { field: 'ai' })),
    label('Dinero', number(data.money, 0, 255, { field: 'money' })),
  );

  const flagInput = Object.assign(document.createElement('input'), { type: 'checkbox', checked: data.flag });
  flagInput.dataset.field = 'flag';
  head.append(label('Flag', flagInput));

  const payload = card('Datos opcionales');
  payload.classList.add('trainer-meta');
  const hasItems = Object.assign(document.createElement('input'), {
    type: 'checkbox', checked: data.hasItems ?? true, disabled: !gen6,
  });
  const hasMoves = Object.assign(document.createElement('input'), {
    type: 'checkbox', checked: data.hasMoves ?? true, disabled: !gen6,
  });
  hasItems.dataset.field = 'hasItems';
  hasMoves.dataset.field = 'hasMoves';
  payload.append(
    label('Nombre de clase', textInput(data.className, { field: 'className' })),
    label(`Guarda objetos${gen6 ? '' : ' (solo Gen VI)'}`, hasItems),
    label(`Guarda movimientos${gen6 ? '' : ' (solo Gen VI)'}`, hasMoves),
  );

  const items = card('Objetos de combate');
  items.classList.add('trainer-items');
  data.items.forEach((value, index) => items.append(
    label(`Objeto ${index + 1}`, select(catalog.items, value, { item: index })),
  ));
  box.append(head, payload, items);

  const team = document.createElement('div');
  team.className = 'trainer-team';
  data.team.forEach((poke, index) => {
    const node = card(`Pokémon ${index + 1}`);
    node.append(
      label('Especie', select(catalog.species, poke.species, { poke: index, field: 'species' })),
      label('Forma', number(poke.form, 0, gen6 ? 65535 : 255, { poke: index, field: 'form' })),
      label('Nivel', number(poke.level, 1, 100, { poke: index, field: 'level' })),
      label('Objeto', select(catalog.items, poke.item, { poke: index, field: 'item' })),
      label('Habilidad', number(poke.ability, 0, gen6 ? 15 : 3, { poke: index, field: 'ability' })),
      label('Género', number(poke.gender, 0, gen6 ? 7 : 3, { poke: index, field: 'gender' })),
      label(`Naturaleza${gen6 ? ' (solo Gen VII)' : ''}`,
        number(poke.nature, 0, 25, { poke: index, field: 'nature' }, gen6)),
    );

    const shinyInput = Object.assign(document.createElement('input'), {
      type: 'checkbox', checked: poke.shiny, disabled: gen6,
    });
    shinyInput.dataset.poke = index;
    shinyInput.dataset.field = 'shiny';
    node.append(label(`Shiny${gen6 ? ' (solo Gen VII)' : ''}`, shinyInput));

    const moves = document.createElement('div');
    moves.className = 'trainer-moves';
    poke.moves.forEach((move, moveIndex) => moves.append(
      label(`Mov. ${moveIndex + 1}`, select(catalog.moves, move, {
        poke: index, array: 'moves', index: moveIndex,
      })),
    ));
    node.append(moves);

    const stats = document.createElement('div');
    stats.className = 'trainer-stats';
    poke.ivs.forEach((value, statIndex) => stats.append(
      label(`IV ${statIndex + 1}`, number(value, 0, gen6 ? 255 : 31, {
        poke: index, array: 'ivs', index: statIndex,
      })),
    ));
    poke.evs.forEach((value, statIndex) => stats.append(
      label(`EV ${statIndex + 1}${gen6 ? ' (solo Gen VII)' : ''}`, number(value, 0, 255, {
        poke: index, array: 'evs', index: statIndex,
      }, gen6)),
    ));
    node.append(stats);
    team.append(node);
  });
  box.append(team);
  ui();
}

async function load() {
  try {
    const [inspect, cat] = await Promise.all([
      post('/api/workspace/inspect', { workspacePath: $('workspace').value }),
      post('/api/editors/trainers/catalog', { workspacePath: $('workspace').value, language: 1 }),
    ]);
    game = inspect;
    catalog = cat;
    const selectTrainer = $('trainer');
    selectTrainer.replaceChildren();
    for (const trainer of cat.trainers)
      selectTrainer.add(new Option(`${trainer.id} · ${trainer.name}`, trainer.id));
    selectTrainer.disabled = false;
    $('open').disabled = false;
    $('editor').classList.remove('is-disabled');
    msg('status', `${inspect.gameVersion} cargado.`, inspect.titleId ? 'success' : 'error');
  } catch (error) {
    msg('status', error.message, 'error');
  }
  ui();
}

async function open() {
  try {
    const response = await post('/api/editors/trainers/entry', {
      workspacePath: $('workspace').value, trainerIndex: +$('trainer').value, language: 1,
    });
    const entry = response.entry;
    data = {
      trainerIndex: response.trainerIndex,
      ...entry,
      team: entry.team.map(poke => ({ ...poke, ivs: poke.iVs ?? poke.ivs, evs: poke.eVs ?? poke.evs })),
    };
    original = structuredClone(data);
    render();
    msg('result', 'Entrenador abierto.');
  } catch (error) {
    msg('result', error.message, 'error');
  }
}

$('browse').onclick = async () => {
  try {
    $('workspace').value = (await post('/api/workspace/pick')).path;
    load();
  } catch (error) { msg('status', error.message, 'error'); }
};
$('load').onclick = load;
$('open').onclick = open;
$('details').oninput = event => {
  const field = event.target.dataset;
  if (!data) return;
  if (field.poke !== undefined) {
    const poke = data.team[+field.poke];
    if (field.array !== undefined) poke[field.array][+field.index] = +event.target.value;
    else poke[field.field] = +event.target.value;
  } else if (field.item !== undefined) data.items[+field.item] = +event.target.value;
  else if (field.field) {
    data[field.field] = field.field === 'name' || field.field === 'className'
      ? event.target.value : +event.target.value;
    if (field.field === 'trainerClass') {
      data.className = catalog.classes.find(item => item.id === data.trainerClass)?.name ?? data.className;
      render();
      return;
    }
  }
  ui();
};
$('details').onchange = event => {
  const field = event.target.dataset;
  if (!data) return;
  if (event.target.type === 'checkbox') {
    if (field.poke !== undefined) data.team[+field.poke][field.field] = event.target.checked;
    else data[field.field] = event.target.checked;
    ui();
    return;
  }
  if (event.target.tagName === 'SELECT') $('details').oninput(event);
};
$('export').onclick = async () => {
  try {
    const out = await post('/api/workspace/pick-output');
    msg('result', 'Exportando entrenador…');
    const { trainerIndex, ...entry } = data;
    const result = await post('/api/editors/trainers/export', {
      workspacePath: $('workspace').value, outputDirectory: out.path, titleId: game.titleId,
      trainerIndex, entry, language: 1,
    });
    original = structuredClone(data);
    msg('result', `Listo. ZIP: ${result.zipPath}`, 'success');
    ui();
  } catch (error) { msg('result', error.message, 'error'); }
};
ui();
