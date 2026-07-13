/// <reference types="vite/client" />

// Caso queira declarar importações genéricas via alias
declare module '@/*' {
  const value: any;
  export default value;
}
