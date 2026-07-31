// Caminho: src/lib/utils.ts
import { type ClassValue, clsx } from "clsx"
import { twMerge } from "tailwind-merge"


/**
 * Uma função auxiliar para fundir condicionalmente classes do Tailwind CSS.
 * * @param inputs Uma sequência de valores de classe. Podem ser strings, objetos ou arrays.
 * @returns Uma string de classes CSS otimizada e sem conflitos.
 */
export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}