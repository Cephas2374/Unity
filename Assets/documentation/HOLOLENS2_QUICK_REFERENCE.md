# HoloLens 2 Quick Reference Card

## 📱 Building Interaction

### 👁️ View Building Data
**Gesture:** Quick Air Tap / Pinch  
**Action:** Point hand ray at building → Quick pinch with thumb and index finger  
**Result:** Energy data panel appears for 10 seconds

### ✏️ Edit Building Attributes
**Gesture:** Hold Air Tap / Sustained Pinch  
**Action:** Point hand ray at building → Hold pinch for 0.5+ seconds  
**Result:** Building attributes edit form opens

---

## 🎮 Navigation

### 🚶 Movement
- **Walk naturally** - Application follows your physical movement
- **Turn your head** - Camera automatically follows gaze direction
- **Room-scale** - Designed for walking around buildings

### 🖐️ Hand Ray Targeting
- **Direct your palm** forward to project hand ray
- **Look at target** for combined gaze + hand interaction
- **Both hands** work for interaction (left or right)

---

## 🎛️ Gesture Guide

### Air Tap (Select)
1. Raise hand, palm facing you
2. Pinch thumb and index finger together
3. Release quickly

### Hold (Long Press)
1. Raise hand, palm facing you
2. Pinch thumb and index finger together
3. **Hold for 0.5+ seconds**
4. Release when form appears

### Dismiss UI
- **Air tap away** from UI panel
- **Walk away** - UI stays at location
- UI auto-hides after 10 seconds

---

## ⚠️ Calibration Tips

If gestures aren't working properly:

1. **Recalibrate Eye Tracking**
   - Say "Settings" or use Start menu
   - System → Calibration → Run eye calibration

2. **Check Hand Tracking**
   - Keep hands in field of view (60° cone)
   - Ensure good lighting (avoid direct sunlight)
   - Keep fingers visible and separated

3. **Optimal Distance**
   - Buildings: 1-10 meters away
   - Hand gestures: 20-60cm from face
   - UI panels: 40-75cm from eyes

---

## 🔊 Voice Commands (Coming Soon)

- "View Data" - Show building information
- "Edit Building" - Open edit form
- "Hide Panel" - Close UI
- "Refresh" - Reload data

---

## 💡 Pro Tips

✅ **DO:**
- Calibrate HoloLens when you first wear it
- Use deliberate, clear gestures
- Wait for visual feedback before next action
- Keep hands within tracking volume

❌ **DON'T:**
- Rush through gestures
- Hold hands too close to face (<20cm)
- Cover hand with other hand
- Gesture in poorly lit areas

---

## 🐛 Quick Troubleshooting

| Issue | Solution |
|-------|----------|
| Gestures not detected | Lower hand, then raise again into view |
| Building not responding | Ensure you're looking at the building while gesturing |
| Panel won't open | Try holding gesture slightly longer (0.7s+) |
| App is slow | Too many buildings loaded - move to simpler area |
| Buildings appear gray | No internet connection - check WiFi |

---

## 📞 Support

**Log Location:** Settings → System → File Explorer → LocalAppData → logs  
**Device Portal:** http://YOUR-HOLOLENS-IP:10080  
**Console Access:** Connect via USB → Visual Studio → Debug

---

*For full documentation, see HOLOLENS2_SETUP_GUIDE.md*
