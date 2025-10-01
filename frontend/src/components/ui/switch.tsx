import * as React from "react"
import { cn } from "../../lib/utils"

interface SwitchProps extends React.HTMLAttributes<HTMLDivElement> {
  checked: boolean
  onCheckedChange: (checked: boolean) => void
  disabled?: boolean
}

const Switch = React.forwardRef<HTMLDivElement, SwitchProps>(
  ({ className, checked, onCheckedChange, disabled = false, ...props }, ref) => {
    return (
      <div
        role="switch"
        aria-checked={checked}
        className={cn(
          "relative inline-flex h-7 w-16 min-w-[4rem] flex-shrink-0 cursor-pointer rounded-full transition-colors",
          checked ? "bg-primary" : "bg-gray-500",
          disabled ? "cursor-not-allowed opacity-50" : "",
          className
        )}
        onClick={() => !disabled && onCheckedChange(!checked)}
        ref={ref}
        {...props}
      >
        {/* Text labels */}
        <div className="absolute inset-0 flex items-center justify-between px-3 text-xs font-bold text-white pointer-events-none">
          <span className={checked ? "opacity-100" : "opacity-0"}>ON</span>
          <span className={checked ? "opacity-0" : "opacity-100"}>OFF</span>
        </div>
        
        {/* Thumb */}
        <div
          className={cn(
            "absolute flex h-5 w-5 items-center justify-center rounded-full bg-black transition-transform",
            "top-[4px]",
            checked ? "translate-x-[36px]" : "translate-x-[4px]"
          )}
        />
      </div>
    )
  }
)

Switch.displayName = "Switch"

export { Switch }
