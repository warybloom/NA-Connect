import * as React from "react"
import { useEffect, useState } from "react";


const MOBILE_BREAKPOINT = 768

export function useIsMobile() {
  const [isMobile, setIsMobile] = React.useState<boolean | undefined>(undefined)

  React.useEffect(() => {
    const mql = window.matchMedia(`(max-width: ${MOBILE_BREAKPOINT - 1}px)`)
    const onChange = () => {
      setIsMobile(window.innerWidth < MOBILE_BREAKPOINT)
    }
    mql.addEventListener("change", onChange)
    setIsMobile(window.innerWidth < MOBILE_BREAKPOINT)
    return () => mql.removeEventListener("change", onChange)
  }, [])

  return !!isMobile
}

interface CurrentUser{
    name: string
    email: string
    avatar: string
}
export function useCurrentUser(){
    const[user, setUser] = useState<CurrentUser | null>(null)
    const[loading, setLoading] = useState(true)
    
useEffect(() => {
    fetch("/Authentication/OAuth/GetProfile", { credentials: "include" })
        .then((res) => {
        if (!res.ok) throw new Error("Não autenticado")
        return res.json()
        })
        .then((data: CurrentUser) => setUser(data))
        .catch(() => setUser(null))
        .finally(() => setLoading(false))
    }, [])
}

