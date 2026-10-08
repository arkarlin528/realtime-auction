import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { api } from '../api/client'

const TYPES = ['20GP', '40GP', '40HC', '45HC', '20RF', '40RF']
const CONDITIONS = ['New (one-trip)', 'Cargo-worthy (CW)', 'Wind & watertight (WWT)', 'As-is']

function localInput(minutesFromNow: number) {
  const d = new Date(Date.now() + minutesFromNow * 60_000)
  return new Date(d.getTime() - d.getTimezoneOffset() * 60_000).toISOString().slice(0, 16)
}

export function NewAuctionPage() {
  const navigate = useNavigate()
  const [form, setForm] = useState({
    title: '40ft High Cube, cargo-worthy, Laem Chabang',
    description: '',
    containerType: '40HC',
    location: 'Laem Chabang',
    condition: CONDITIONS[1],
    yearBuilt: 2018,
    startingPrice: 1500,
    minIncrement: 50,
    reservePrice: '',
    startsAt: localInput(1),
    endsAt: localInput(11),
  })
  const [error, setError] = useState<string | null>(null)
  const set = (key: keyof typeof form) => (e: { target: { value: string } }) => setForm(f => ({ ...f, [key]: e.target.value }))

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setError(null)
    try {
      const created = await api.createAuction({
        ...form,
        yearBuilt: Number(form.yearBuilt),
        startingPrice: Number(form.startingPrice),
        minIncrement: Number(form.minIncrement),
        reservePrice: form.reservePrice === '' ? null : Number(form.reservePrice),
        startsAt: new Date(form.startsAt).toISOString(),
        endsAt: new Date(form.endsAt).toISOString(),
      })
      navigate(`/auctions/${created.id}`)
    } catch (err) {
      setError((err as Error).message)
    }
  }

  return (
    <>
      <div className="page-head"><h1>Schedule an auction</h1></div>
      <form className="card form-grid" onSubmit={onSubmit}>
        <label className="wide">Title<input value={form.title} onChange={set('title')} required maxLength={150} /></label>
        <label>Type<select value={form.containerType} onChange={set('containerType')}>{TYPES.map(t => <option key={t}>{t}</option>)}</select></label>
        <label>Condition<select value={form.condition} onChange={set('condition')}>{CONDITIONS.map(c => <option key={c}>{c}</option>)}</select></label>
        <label>Location<input value={form.location} onChange={set('location')} required /></label>
        <label>Year built<input type="number" value={form.yearBuilt} onChange={set('yearBuilt')} /></label>
        <label>Starting price (USD)<input type="number" value={form.startingPrice} onChange={set('startingPrice')} /></label>
        <label>Minimum increment<input type="number" value={form.minIncrement} onChange={set('minIncrement')} /></label>
        <label>Reserve <small>(optional, hidden from bidders)</small><input type="number" value={form.reservePrice} onChange={set('reservePrice')} /></label>
        <span />
        <label>Starts<input type="datetime-local" value={form.startsAt} onChange={set('startsAt')} /></label>
        <label>Ends<input type="datetime-local" value={form.endsAt} onChange={set('endsAt')} /></label>
        <label className="wide">Description<textarea rows={3} value={form.description} onChange={set('description')} /></label>
        {error && <div className="error wide">{error}</div>}
        <div className="wide buttons"><button className="btn">Schedule auction</button></div>
      </form>
    </>
  )
}
