// Convert established vector artwork to runtime PNGs. Original SVGs remain authoritative.
const path = require('path');
const fs = require('fs');
const packageRoot = process.env.SOCCER_NODE_MODULES || path.join(require('os').homedir(), '.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules');
const sharp = require(path.join(packageRoot, 'sharp'));
const root = path.resolve(__dirname, '..');
const source = path.join(root, 'assets/original/vector');
const target = path.join(root, 'UnityProject/Assets/Soccer/Art/Arena');
fs.mkdirSync(target, { recursive: true });
(async () => {
    for (const file of fs.readdirSync(source).filter(file => file.endsWith('-v2.svg'))) {
        await sharp(path.join(source, file)).png().toFile(path.join(target, file.replace('.svg', '.png')));
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
