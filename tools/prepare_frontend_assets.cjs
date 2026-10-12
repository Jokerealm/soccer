// Reuse the existing mode icon system and preserve the generated splash original.
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const root = path.resolve(__dirname, '..');
const packageRoot = process.env.SOCCER_NODE_MODULES || path.join(require('os').homedir(), '.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules');
const sharp = require(path.join(packageRoot, 'sharp'));
const target = path.join(root, 'UnityProject/Assets/Soccer/Resources/FrontEnd');

(async () => {
    fs.mkdirSync(target, { recursive: true });
    const source = 'assets/original/ui/launch-cover-v1.png';
    fs.copyFileSync(path.join(root, source), path.join(target, 'launch-cover-v1.png'));
    const assets = [{ source, target: 'UnityProject/Assets/Soccer/Resources/FrontEnd/launch-cover-v1.png',
        sha256: crypto.createHash('sha256').update(fs.readFileSync(path.join(root, source))).digest('hex') }];
    for (const [input, output] of [['standard', 'normal'], ['ranked', 'ranked'], ['party', 'party']]) {
        const from = `assets/original/vector/mode-${input}-v1.svg`;
        const to = `UnityProject/Assets/Soccer/Resources/FrontEnd/mode-${output}.png`;
        await sharp(path.join(root, from)).resize(192, 192).png().toFile(path.join(root, to));
        assets.push({ source: from, target: to, conversion: 'Rasterized established SVG; source retained' });
    }
    fs.writeFileSync(path.join(target, 'provenance.json'), JSON.stringify({
        version: '0.3', splash: 'Built-in image_gen, generated with three existing character identity references; prompt in assets/original/ui/generation-prompt.json', assets
    }, null, 2) + '\n');
    console.log('Prepared launch cover and three existing mode icons.');
})().catch(error => { console.error(error); process.exitCode = 1; });
