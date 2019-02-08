# SoundManager component for Unity3D

My take on a sound manager in Unity3D. Admittedly I haven't looked into any implementations on the Asset Store, etc., because I didn't want to spoil the fun of writing mine from scratch.

I've been using it in my project for quite some time, and I'm a satisfied customer, so to speak, so I decided to share it.

Pro tip: It's totally not spaghetti code, like most things you can find in relation to Unity. ;) It's not perfect either, though, but I didn't want to fragment it too much, since the added method call overhead, however minor, is not that useful in games.

*(Never mind that when I started to 'polish it up' for sharing it was merely 165 lines long... It's still small with around 400 lines, but not as tiny and cute anymore.)*

## Quick overview of Inspector pane:

![SoundManager pane in Inspector](SoundManager-Inspector-example.png)

## Rationale

Pretty much everybody uses some sort of audio or sound manager, from what I'm aware of. But if you're not sure what's the point:

- ### Playing sounds on destroyed/disabled objects
  - If you have an AudioSource on a GameObject, and you destroy it (or ideally, disable for releasing into the pool), you can't play any sounds on it, since sound playback stops instantly.
- ### Playing sounds with modulated pitch/volume
  - Sounds sound the best if you slightly modulate the pitch and volume each time you play them, to make them feel natural. It's messy to do this individually everywhere.

## Features

- ### Customizable random pitch and volume range for each AudioClip
  - You can set up the range of random pitch and volume for each audioclip at a centralized location. Then you just simply play the sound by referencing it as an `enum` value, for example:
 
     `SoundManager.Instance.PlaySound(GameSound.Death)`
  
- ### Smart, automatic pooling of AudioSources
  - You can define how many simultaneous sounds you want to support at startup. The `SoundManager` automatically reserves them from the pool to play the requested sound, waits for the playback to stop, and then puts the `AudioSource` back to the pool. No polling involved whatsoever. Coroutine-based operation. No wasteful use of collections, it uses a simple `Stack` the way it's supposed to be used.
 
- ### Support for 3D positioned AudioSources
  - My current game is 2D, so I have no use for this, but I wanted to add this little extra before sharing it. Basically, you can simply use an overloaded version of the `PlaySound()` method that accepts a `Vector3` position defining where to play the sound. So, for example:
 
    `SoundManager.Instance.PlaySound(GameSound.Death, transform.position)`
 
   - When the playback is complete, the `AudioSource` will be instantly put back to its original place. There is no expensive reparenting involved; the `SoundManager` simply creates a `GameObject` for each pooled `AudioSource`, so it can position them anywhere.
   
- ### Overriding preset pitch and volume
  - There is an overloaded version of the `PlaySound()` method accepting two floats which serve as multipliers to pitch and volume. So if you find yourself wanting to play a faster/slower or louder/quieter sound than normal, or play it reverse by using a negative pitch, you can.
  
- ### Callback when playback is finished
  - All overloads of the `PlaySound()` method accept an optional `callback` parameter, in case you want to be notified when the playback finishes.
  - Additionally, all methods return the `AudioSource` playing your requested sound, so you can monitor it yourself if you want. But don't mess with the playback settings on the returned `AudioSource`, because then the `SoundManager` won't be able to predict the end time of the playback (to release the `AudioSource` to the pool). (It does have built-in safety mechanism for additional waiting, though.)
  

- ### Thoroughly commented and documented code
  - I added standard XML documentation tags to all public methods, so Visual Studio's IntelliSense can help you understand the what do methods and parameters do.
  - Also, the code contains lots of comments, including on all private methods and everywhere where something might not be obvious. I think I went a bit overboard, because I know that many Unity3D users are not that well-versed in programming.
