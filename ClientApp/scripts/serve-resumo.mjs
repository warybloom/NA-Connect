import {preview} from 'vite';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const target='http://127.0.0.1:5288';
await preview({configFile:false,root,build:{outDir:path.resolve(root,'../_build_verify/resumo-vite')},
  preview:{host:'127.0.0.1',port:5289,strictPort:true,proxy:Object.fromEntries(['/api','/Authentication','/DataManagement'].map(prefix=>[prefix,{target,changeOrigin:true,secure:false}]))}});
console.log('Resumo test frontend: http://localhost:5289 ; backend: http://localhost:5288 ; startup worker disabled by deployment');
