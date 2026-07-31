// Caminho: ClientApp/src/components/ui/logo.tsx
// AJUSTE A ALTURA DA IMAGEM

import { cn } from "@/lib/utils";

interface LogoProps {
  className?: string;
}

export function Logo({ className }: LogoProps) {
  return (
    <div className={cn("flex items-center", className)}>
      <img
        src="/logo-na-30anos.png"
        alt="N&A Consultores 30 Anos"
        // 👇👇 AJUSTE ESTA ALTURA CONFORME NECESSÁRIO 👇👇
        className="h-20 w-auto object-contain invert dark:invert-0 transition-all mt-5" // h-12 = 3rem. Tente h-10, h-14, etc.
      />
    </div>
  );
}