const $ = id => document.getElementById(id);
let game, table, original;

async function post(url, body = {}) {
  const response = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  const data = await response.json();
  if (!response.ok) throw Error(data.error || data.detail || 'Operación fallida');
  return data;
}

function changed() { return !!table && (+$('rerolls').value !== table.rerolls || $('everything-shiny').checked !== table.everythingShiny); }
function ui() {
  $('summary').textContent = changed() ? 'Cambios preparados' : 'Sin cambios para exportar';
  $('export').disabled = !game?.titleId || !changed();
}
function render() {
  $('rerolls').value = table.rerolls;
  $('everything-shiny').checked = table.everythingShiny;
  $('supported').textContent = `Valores soportados: ${table.supportedRerolls.slice(0, 12).join(', ')} … ${table.supportedRerolls.at(-1)}.`;
  $('warning').textContent = table.warning;
  ui();
}
async function load() {
  try {
    game = await post('/api/workspace/inspect', { workspacePath: $('workspace').value });
    $('editor').classList.remove('is-disabled');
    $('status').textContent = `${game.gameVersion} cargado.`;
  } catch (error) { $('status').textContent = error.message; }
  ui();
}
async function open() {
  try {
    table = await post('/api/editors/shiny-rate/table', { workspacePath: $('workspace').value, language: 1 });
    original = structuredClone(table);
    render();
  } catch (error) { $('result').textContent = error.message; }
}
$('browse').onclick = async () => {
  try { $('workspace').value = (await post('/api/workspace/pick')).path; await load(); }
  catch (error) { $('status').textContent = error.message; }
};
$('load').onclick = load;
$('rerolls').oninput = ui;
$('everything-shiny').onchange = ui;
$('export').onclick = async () => {
  try {
    const output = await post('/api/workspace/pick-output');
    const result = await post('/api/editors/shiny-rate/export', { workspacePath: $('workspace').value, outputDirectory: output.path, titleId: game.titleId, rerolls: +$('rerolls').value, everythingShiny: $('everything-shiny').checked, language: 1 });
    table.rerolls = +$('rerolls').value;
    table.everythingShiny = $('everything-shiny').checked;
    original = structuredClone(table);
    $('result').textContent = `Listo. ZIP: ${result.zipPath}`;
    ui();
  } catch (error) { $('result').textContent = error.message; }
};
ui();
