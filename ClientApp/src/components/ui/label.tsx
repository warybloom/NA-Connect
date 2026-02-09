import React from "react";

interface LabelProps {
  htmlFor?: string;
  children: React.ReactNode;
  className?: string;
}

export const Label = ({ htmlFor, children, className = "" }: LabelProps) => (
  <label htmlFor={htmlFor} className={`block text-sm font-medium text-foreground ${className}`}>
    {children}
  </label>
);
