import { useEffect, useRef, useState } from 'react'

/** True for a moment after the value changes, to draw the eye to live updates. */
export function useFlashOnChange(value: unknown): boolean {
  const [flash, setFlash] = useState(false)
  const previous = useRef(value)
  useEffect(() => {
    if (previous.current === value) return
    previous.current = value
    setFlash(true)
    const timer = setTimeout(() => setFlash(false), 900)
    return () => clearTimeout(timer)
  }, [value])
  return flash
}
