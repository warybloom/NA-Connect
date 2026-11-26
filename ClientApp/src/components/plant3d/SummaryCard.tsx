import { LucideIcon } from "lucide-react";

interface SummaryCardProps {
  title: string;
  value: string | number;
  icon: LucideIcon;
  variant?: "default" | "alert";
}

const SummaryCard = ({ title, value, icon: Icon, variant = "default" }: SummaryCardProps) => {
  const isAlert = variant === "alert";

  return (
    <div
      className={`glass-panel p-5 transition-all duration-200 hover:-translate-y-0.5 hover:shadow-lg ${
        isAlert ? "border-destructive/50 hover:border-destructive" : "hover:border-primary/50"
      }`}
    >
      <div className="flex items-center justify-between">
        <div>
          <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
            {title}
          </p>
          <p className={`mt-2 text-3xl font-bold ${isAlert ? "text-destructive animate-pulse-glow" : "text-foreground"}`}>
            {value}
          </p>
        </div>
        <div className={`rounded-lg p-3 ${isAlert ? "bg-destructive/10 text-destructive" : "bg-primary/10 text-primary"}`}>
          <Icon className="h-6 w-6" />
        </div>
      </div>
    </div>
  );
};

export default SummaryCard;
