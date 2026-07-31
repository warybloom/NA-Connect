import { useEffect, useState } from "react";

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
    return {user, loading}
}


