// Original weapon symbols for the two gameplay prototypes; no hero likenesses.
const fs = require('fs'), path = require('path'), os = require('os');
const sharp = require(path.join(os.homedir(), '.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp'));
const root = path.resolve(__dirname, '..');
const source = path.join(root, 'assets/original/vector/star-strikers');
const output = path.join(root, 'UnityProject/Assets/Soccer/Resources/FrontEnd');
const defs = '<defs><linearGradient id="metal" x2=".3" y2="1"><stop stop-color="#f0f6e6"/><stop offset=".45" stop-color="#bed9cd"/><stop offset="1" stop-color="#5e94a0"/></linearGradient><linearGradient id="teal" x2=".8" y2="1"><stop stop-color="#8ff0c3"/><stop offset="1" stop-color="#247786"/></linearGradient><linearGradient id="gold" x2=".7" y2="1"><stop stop-color="#ffdf91"/><stop offset="1" stop-color="#b66a2c"/></linearGradient></defs>';
const icons = {
  'equipment-marine': '<ellipse cx="192" cy="310" rx="122" ry="18" fill="#163d4d" opacity=".12"/><g transform="rotate(-22 192 180)" stroke="#315e6b" stroke-width="7" stroke-linejoin="round"><path d="M80 136h151l22 28h69v53h-74l-18 28H80z" fill="url(#metal)"/><path d="M68 132h44v113H68zM156 236h46l-12 60h-42z" fill="url(#teal)"/><path d="M216 167h82v16h-82z" fill="url(#gold)"/><path d="M161 123v-28h52v28" fill="none"/><path d="M109 156h53m-53 26h53m51 29h42" stroke="#effbe9" stroke-width="10" stroke-linecap="round"/></g>',
  'equipment-thor': '<ellipse cx="192" cy="321" rx="130" ry="18" fill="#163d4d" opacity=".12"/><g stroke="#3e6270" stroke-width="7" stroke-linejoin="round"><path d="M103 169h178l18 96-107 39-107-39z" fill="url(#metal)"/><path d="M108 85h52v114h-52zM224 85h52v114h-52z" fill="url(#gold)"/><path d="M101 73h66v38h-66zM217 73h66v38h-66z" fill="url(#teal)"/><path d="M141 201h103l-9 54h-85z" fill="url(#teal)"/><path d="M178 218h28v23h-28z" fill="#edffcd" stroke="none"/><path d="M105 269l-9 30m183-30 9 30" stroke-width="20" stroke-linecap="round"/><path d="M114 86h42m68 0h42" stroke="#fff0c1" stroke-width="9"/></g>'
};
(async () => {
  fs.mkdirSync(source, { recursive: true });
  for (const [name, body] of Object.entries(icons)) {
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="384" height="384" viewBox="0 0 384 384">${defs}${body}</svg>`;
    fs.writeFileSync(path.join(source, name + '.svg'), svg);
    await sharp(Buffer.from(svg)).resize(768, 768).png().toFile(path.join(output, name + '.png'));
  }
  console.log('Two original weapon icons exported at 768 px.');
})().catch(error => { console.error(error); process.exitCode = 1; });
