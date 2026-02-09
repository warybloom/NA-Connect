// Caminho: ClientApp/src/components/ui/elegant-card.tsx (COM A CORREÇÃO FINAL)

import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"

interface ElegantCardProps {
  title: string
  description?: string
  children?: React.ReactNode
  className?: string
  buttonText?: string
  onButtonClick?: () => void
  showButton?: boolean
}

export function ElegantCard({ 
  title, 
  description, 
  children, 
  className,
  buttonText = "Abrir",
  onButtonClick,
  showButton = true
}: ElegantCardProps) {
  return (
    <Card className={cn("glass-card hover-lift group flex flex-col", className)}>
      <CardHeader className="p-4">
        <CardTitle className="text-lg font-semibold text-foreground">{title}</CardTitle>
        {description && (
          <CardDescription className="text-sm text-muted-foreground pt-1">
            {description}
          </CardDescription>
        )}
      </CardHeader>
      <CardContent className="p-4 pt-0 flex-grow flex flex-col justify-end">
        {children}
        {showButton && (
          <Button 
            // ✅ MUDANÇA CRÍTICA: Trocamos a variante "elegant" pela "default"
            // para garantir que o botão seja sempre visível.
            variant="default" 
            className="w-full mt-4"
            onClick={onButtonClick}
          >
            {buttonText}
          </Button>
        )}
      </CardContent>
    </Card>
  )
}