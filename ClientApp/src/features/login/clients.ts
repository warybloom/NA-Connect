// Ficheiro: ClientApp/src/features/login/clients.ts
// Clientes exibidos na faixa inferior do login. Única fonte da lista: adicionar/remover um cliente é
// editar este array e colocar o arquivo em ClientApp/public/brands/clients/.
//
// Logos: SVG ou PNG transparente de preferência, recortados sem margem vazia.
// width/height = dimensões do arquivo em pixels; definem a proporção e o tamanho óptico na faixa.
// tone (como o logo é convertido para branco sobre o fundo escuro):
//   "mono"     (padrão)  PNG/SVG transparente: silhueta branca.
//   "knockout"           fundo claro opaco (JPEG) ou texto vazado em branco: o claro some, o resto fica branco.
//   "original"           cores originais, para marcas que perdem identidade nos modos acima.
//
// Origem dos arquivos: pasta "Clientes" do LAB (originais preservados lá).

export interface ClientBrand {
  name: string;
  src: string;
  alt: string;
  width: number;
  height: number;
  tone?: "mono" | "knockout" | "original";
}

export const CLIENTS: ClientBrand[] = [
  { name: "Acelen", src: "/brands/clients/acelen.png", alt: "Acelen", width: 262, height: 48 },
  { name: "AD Shopping", src: "/brands/clients/ad-shopping.jpg", alt: "AD Shopping", width: 167, height: 132, tone: "knockout" },
  { name: "ADM", src: "/brands/clients/adm.webp", alt: "ADM", width: 300, height: 225 },
  { name: "AES Brasil", src: "/brands/clients/aes-brasil.jpg", alt: "AES Brasil", width: 300, height: 63, tone: "knockout" },
  { name: "ALLOS", src: "/brands/clients/allos.png", alt: "ALLOS", width: 285, height: 77 },
  { name: "Amyris", src: "/brands/clients/amyris.jpg", alt: "Amyris", width: 135, height: 132, tone: "knockout" },
  { name: "Ancar Ivanhoe", src: "/brands/clients/ancar-ivanhoe.png", alt: "Ancar Ivanhoe", width: 300, height: 90 },
  { name: "Asperbras", src: "/brands/clients/asperbras.jpg", alt: "Asperbras", width: 293, height: 68, tone: "knockout" },
  { name: "BAT", src: "/brands/clients/bat.png", alt: "BAT", width: 300, height: 128 },
  { name: "BAY Ltd.", src: "/brands/clients/bay.png", alt: "BAY Ltd.", width: 228, height: 132 },
  { name: "Bayer", src: "/brands/clients/bayer.png", alt: "Bayer", width: 300, height: 300 },
  { name: "Biosev", src: "/brands/clients/biosev.jpg", alt: "Biosev", width: 300, height: 137, tone: "knockout" },
  { name: "Boston Metal", src: "/brands/clients/boston-metal.png", alt: "Boston Metal", width: 296, height: 80 },
  { name: "bp bunge bioenergia", src: "/brands/clients/bp-bunge.png", alt: "bp bunge bioenergia", width: 294, height: 121 },
  { name: "Brasil BioFuels", src: "/brands/clients/brasil-biofuels.jpg", alt: "Brasil BioFuels", width: 289, height: 118, tone: "knockout" },
  { name: "Braskem", src: "/brands/clients/braskem.webp", alt: "Braskem", width: 300, height: 90 },
  { name: "BTG Pactual", src: "/brands/clients/btg-pactual.png", alt: "BTG Pactual", width: 300, height: 119 },
  { name: "Bunge", src: "/brands/clients/bunge.png", alt: "Bunge", width: 300, height: 69 },
  { name: "BYD", src: "/brands/clients/byd.png", alt: "BYD", width: 278, height: 58 },
  { name: "Casa Forte Investimentos", src: "/brands/clients/casa-forte.jpg", alt: "Casa Forte Investimentos", width: 277, height: 60, tone: "knockout" },
  { name: "CC Polymers", src: "/brands/clients/cc-polymers.jpg", alt: "CC Polymers", width: 252, height: 58, tone: "knockout" },
  { name: "Cencosud", src: "/brands/clients/cencosud.png", alt: "Cencosud", width: 252, height: 132 },
  { name: "Centauro", src: "/brands/clients/centauro.jpg", alt: "Centauro", width: 300, height: 61, tone: "knockout" },
  { name: "Claro", src: "/brands/clients/claro.jpg", alt: "Claro", width: 132, height: 132, tone: "original" },
  { name: "Coca-Cola FEMSA", src: "/brands/clients/coca-cola-femsa.jpg", alt: "Coca-Cola FEMSA", width: 278, height: 149, tone: "knockout" },
  { name: "Colpar", src: "/brands/clients/colpar.png", alt: "Colpar", width: 300, height: 96, tone: "knockout" },
  { name: "Conifer Energy", src: "/brands/clients/conifer-energy.png", alt: "Conifer Energy", width: 300, height: 86 },
  { name: "ConocoPhillips", src: "/brands/clients/conocophillips.png", alt: "ConocoPhillips", width: 300, height: 71 },
  { name: "Continental", src: "/brands/clients/continental.png", alt: "Continental", width: 258, height: 50 },
  { name: "Corbion", src: "/brands/clients/corbion.png", alt: "Corbion", width: 300, height: 202 },
  { name: "CPFL Renováveis", src: "/brands/clients/cpfl-renovaveis.jpg", alt: "CPFL Renováveis", width: 189, height: 132, tone: "knockout" },
  { name: "Dasa", src: "/brands/clients/dasa.png", alt: "Dasa", width: 300, height: 82 },
  { name: "Deten Química", src: "/brands/clients/deten-quimica.jpg", alt: "Deten Química", width: 260, height: 76, tone: "knockout" },
  { name: "Dexco", src: "/brands/clients/dexco.png", alt: "Dexco", width: 300, height: 56 },
  { name: "Dow", src: "/brands/clients/dow.jpg", alt: "Dow", width: 300, height: 106, tone: "knockout" },
  { name: "Elera Renováveis", src: "/brands/clients/elera-renovaveis.png", alt: "Elera Renováveis", width: 270, height: 83 },
  { name: "Enel Green Power", src: "/brands/clients/enel-green-power.png", alt: "Enel Green Power", width: 300, height: 150 },
  { name: "Energea Brasil", src: "/brands/clients/energea.png", alt: "Energea Brasil", width: 284, height: 98 },
  { name: "Epasa", src: "/brands/clients/epasa.png", alt: "Epasa", width: 300, height: 105 },
  { name: "ERB Energias Renováveis do Brasil", src: "/brands/clients/erb.jpg", alt: "ERB Energias Renováveis do Brasil", width: 164, height: 64, tone: "knockout" },
  { name: "Estapar", src: "/brands/clients/estapar.png", alt: "Estapar", width: 300, height: 77 },
  { name: "Exterran", src: "/brands/clients/exterran.png", alt: "Exterran", width: 300, height: 127 },
  { name: "Extrafarma", src: "/brands/clients/extrafarma.jpg", alt: "Extrafarma", width: 300, height: 49, tone: "knockout" },
  { name: "Formosa Plastics", src: "/brands/clients/formosa-plastics.png", alt: "Formosa Plastics", width: 300, height: 143 },
  { name: "General Mills", src: "/brands/clients/general-mills.png", alt: "General Mills", width: 300, height: 84, tone: "knockout" },
  { name: "Global Energia", src: "/brands/clients/global-energia.jpg", alt: "Global Energia", width: 287, height: 180, tone: "knockout" },
  { name: "GPE Global Participações em Energia", src: "/brands/clients/gpe.png", alt: "GPE Global Participações em Energia", width: 300, height: 72 },
  { name: "GreenYellow", src: "/brands/clients/greenyellow.jpg", alt: "GreenYellow", width: 221, height: 54, tone: "knockout" },
  { name: "Grupo Carnaúba", src: "/brands/clients/grupo-carnauba.png", alt: "Grupo Carnaúba", width: 300, height: 83 },
  { name: "Grupo Energisa", src: "/brands/clients/grupo-energisa.jpg", alt: "Grupo Energisa", width: 300, height: 98, tone: "knockout" },
  { name: "Grupo Gera", src: "/brands/clients/grupo-gera.png", alt: "Grupo Gera", width: 242, height: 78 },
  { name: "Grupo JCPM", src: "/brands/clients/grupo-jcpm.jpg", alt: "Grupo JCPM", width: 141, height: 132, tone: "knockout" },
  { name: "Gulf Coast Growth Ventures", src: "/brands/clients/gulf-coast-growth-ventures.jpg", alt: "Gulf Coast Growth Ventures", width: 245, height: 61, tone: "knockout" },
  { name: "Hapvida", src: "/brands/clients/hapvida.png", alt: "Hapvida", width: 207, height: 132 },
  { name: "Helexia", src: "/brands/clients/helexia.jpg", alt: "Helexia", width: 183, height: 43, tone: "knockout" },
  { name: "Hydria", src: "/brands/clients/hydria.png", alt: "Hydria", width: 142, height: 111 },
  { name: "IBS Energy", src: "/brands/clients/ibs-energy.jpg", alt: "IBS Energy", width: 300, height: 108, tone: "knockout" },
  { name: "IMPSA", src: "/brands/clients/impsa.jpg", alt: "IMPSA", width: 296, height: 108, tone: "knockout" },
  { name: "Indorama Ventures", src: "/brands/clients/indorama-ventures.png", alt: "Indorama Ventures", width: 287, height: 76 },
  { name: "Infinium", src: "/brands/clients/infinium.jpg", alt: "Infinium", width: 270, height: 84, tone: "knockout" },
  { name: "ivi energia", src: "/brands/clients/ivi-energia.png", alt: "ivi energia", width: 185, height: 67 },
  { name: "Kalunga", src: "/brands/clients/kalunga.png", alt: "Kalunga", width: 300, height: 79 },
  { name: "Largo", src: "/brands/clients/largo.jpg", alt: "Largo", width: 192, height: 45, tone: "knockout" },
  { name: "LDC Louis Dreyfus Company", src: "/brands/clients/ldc.png", alt: "LDC Louis Dreyfus Company", width: 275, height: 131 },
  { name: "Le Biscuit", src: "/brands/clients/le-biscuit.png", alt: "Le Biscuit", width: 300, height: 113, tone: "knockout" },
  { name: "LM Wind Power", src: "/brands/clients/lm-wind-power.png", alt: "LM Wind Power", width: 238, height: 76 },
  { name: "Lone Star Ports", src: "/brands/clients/lone-star-ports.jpg", alt: "Lone Star Ports", width: 177, height: 128, tone: "knockout" },
  { name: "MAX Energy", src: "/brands/clients/max-energy.png", alt: "MAX Energy", width: 300, height: 139 },
  { name: "Merichem", src: "/brands/clients/merichem.jpg", alt: "Merichem", width: 300, height: 163, tone: "knockout" },
  { name: "Mossi Ghisolfi Group", src: "/brands/clients/mossi-ghisolfi.png", alt: "Mossi Ghisolfi Group", width: 191, height: 36 },
  { name: "Newave Energia", src: "/brands/clients/newave-energia.webp", alt: "Newave Energia", width: 282, height: 99 },
  { name: "Oxiteno", src: "/brands/clients/oxiteno.jpg", alt: "Oxiteno", width: 300, height: 101, tone: "knockout" },
  { name: "PetroReconcavo", src: "/brands/clients/petroreconcavo.jpg", alt: "PetroReconcavo", width: 281, height: 73, tone: "knockout" },
  { name: "Pin Oak", src: "/brands/clients/pin-oak.png", alt: "Pin Oak", width: 300, height: 298, tone: "original" },
  { name: "Quirium", src: "/brands/clients/quirium.png", alt: "Quirium", width: 300, height: 74 },
  { name: "Raízen", src: "/brands/clients/raizen.png", alt: "Raízen", width: 300, height: 124 },
  { name: "Rede D'Or São Luiz", src: "/brands/clients/rede-dor.jpg", alt: "Rede D'Or São Luiz", width: 213, height: 132, tone: "knockout" },
  { name: "Rio Energy", src: "/brands/clients/rio-energy.png", alt: "Rio Energy", width: 200, height: 97, tone: "knockout" },
  { name: "Santos Brasil", src: "/brands/clients/santos-brasil.png", alt: "Santos Brasil", width: 300, height: 97 },
  { name: "Saphyr Shopping Centers", src: "/brands/clients/saphyr.jpg", alt: "Saphyr Shopping Centers", width: 236, height: 60, tone: "knockout" },
  { name: "Sensatto Investimentos", src: "/brands/clients/sensatto.jpg", alt: "Sensatto Investimentos", width: 300, height: 99, tone: "knockout" },
  { name: "Solenis", src: "/brands/clients/solenis.png", alt: "Solenis", width: 297, height: 49 },
  { name: "Soltec", src: "/brands/clients/soltec.png", alt: "Soltec", width: 271, height: 139 },
  { name: "São Fernando", src: "/brands/clients/sao-fernando.png", alt: "São Fernando", width: 300, height: 123, tone: "knockout" },
  { name: "Tama", src: "/brands/clients/tama.jpg", alt: "Tama", width: 300, height: 111, tone: "knockout" },
  { name: "TECAB", src: "/brands/clients/tecab.png", alt: "TECAB", width: 223, height: 195, tone: "knockout" },
  { name: "Terphane", src: "/brands/clients/terphane.jpg", alt: "Terphane", width: 300, height: 147, tone: "knockout" },
  { name: "Tesla", src: "/brands/clients/tesla.png", alt: "Tesla", width: 300, height: 102 },
  { name: "TIASA Titânio das Américas", src: "/brands/clients/tiasa.jpg", alt: "TIASA Titânio das Américas", width: 300, height: 90, tone: "knockout" },
  { name: "Tronox", src: "/brands/clients/tronox.jpg", alt: "Tronox", width: 200, height: 45, tone: "knockout" },
  { name: "Ultracargo", src: "/brands/clients/ultracargo.png", alt: "Ultracargo", width: 300, height: 82 },
  { name: "Unigel", src: "/brands/clients/unigel.jpg", alt: "Unigel", width: 247, height: 56, tone: "knockout" },
  { name: "Unimed Fortaleza", src: "/brands/clients/unimed-fortaleza.jpg", alt: "Unimed Fortaleza", width: 294, height: 108, tone: "knockout" },
  { name: "Unipar", src: "/brands/clients/unipar.png", alt: "Unipar", width: 300, height: 102 },
  { name: "Vertoil", src: "/brands/clients/vertoil.png", alt: "Vertoil", width: 275, height: 89 },
  { name: "Vinci Partners", src: "/brands/clients/vinci-partners.png", alt: "Vinci Partners", width: 266, height: 110 },
  { name: "Walmart", src: "/brands/clients/walmart.png", alt: "Walmart", width: 300, height: 75 },
  { name: "Wilson Sons", src: "/brands/clients/wilson-sons.png", alt: "Wilson Sons", width: 300, height: 202 },
];
